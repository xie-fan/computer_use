using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Identity;
using ComputerUse.Mcp.Tests.Fakes;

namespace ComputerUse.Mcp.Tests;

public sealed class AppIdentityFactoryTests
{
    [Fact]
    public void SignedWin32_KeyContainsSigner_NotPath()
    {
        var world = new FakeWorld();
        world.Processes[1] = new FakeProcess
        {
            Pid = 1,
            CreateTimeUtc = 10,
            ImagePath = @"c:\apps\editor\editor.exe",
            SignerSubject = "CN=Example Corp",
            ProductName = "Editor",
            ProductVersion = "2.1.0"
        };

        var key = new AppIdentityFactory(world).Resolve(1, 10, "MainWnd");

        Assert.Contains("CN=Example Corp", key.Value, StringComparison.Ordinal);
        Assert.Contains("Editor", key.Value, StringComparison.Ordinal);
        Assert.Contains("2.1.0", key.Value, StringComparison.Ordinal);
        Assert.Contains("MainWnd", key.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("editor.exe", key.Value, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(@"c:\apps\editor\editor.exe", key.Diagnostics.RawImagePath);
        Assert.Equal("CN=Example Corp", key.Diagnostics.SignerSubject);
    }

    [Fact]
    public void Msix_KeyContainsPfn_NotPath()
    {
        var world = new FakeWorld();
        world.Processes[1] = new FakeProcess
        {
            Pid = 1,
            CreateTimeUtc = 10,
            ImagePath = @"c:\program files\windowsapps\contoso.app\app.exe",
            PackageFamilyName = "Contoso.App_8wekyb3d8bbwe",
            SignerSubject = "CN=Contoso",
            ProductName = "App",
            ProductVersion = "1.0.0"
        };

        var key = new AppIdentityFactory(world).Resolve(1, 10, "WinUI");

        Assert.Contains("Contoso.App_8wekyb3d8bbwe", key.Value, StringComparison.Ordinal);
        Assert.Contains("WinUI", key.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("windowsapps", key.Value, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Contoso.App_8wekyb3d8bbwe", key.Diagnostics.PackageFamilyName);
        Assert.Equal(@"c:\program files\windowsapps\contoso.app\app.exe", key.Diagnostics.RawImagePath);
    }

    [Fact]
    public void ExistingPathPlusClass_MatchesLegacyFourNullKey()
    {
        var path = @"c:\apps\app.exe";
        var world = new FakeWorld();
        world.Processes[1] = new FakeProcess { Pid = 1, CreateTimeUtc = 1, ImagePath = path };

        var produced = new AppIdentityFactory(world).Resolve(1, 1, "Notepad").Value;
        var legacy = AppKeyResolver.Compute(new AppIdentity(null, null, null, null, path, "Notepad")).Value;
        Assert.Equal(legacy, produced);
    }

    [Fact]
    public void PathMissing_NoPfnSignerProduct_ThrowsAppIdentityUnavailable()
    {
        var world = new FakeWorld();
        world.Processes[1] = new FakeProcess { Pid = 1, CreateTimeUtc = 1, ImagePath = null };

        var ex = Assert.Throws<ComputerUseException>(() =>
            new AppIdentityFactory(world).Resolve(1, 1, "Chrome_WidgetWin_1"));
        Assert.Equal(ErrorCodes.AppIdentityUnavailable, ex.Code);
        Assert.DoesNotContain("|Chrome_WidgetWin_1", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_CachesByPidAndCreateTime()
    {
        var world = new FakeWorld();
        world.Processes[1] = new FakeProcess { Pid = 1, CreateTimeUtc = 5, ImagePath = @"c:\apps\app.exe" };
        var factory = new AppIdentityFactory(world);

        _ = factory.Resolve(1, 5, "A");
        _ = factory.Resolve(1, 5, "B");
        Assert.Equal(1, world.SignerSubjectCallCount);
        Assert.Equal(1, world.PackageFamilyNameCallCount);

        _ = factory.Resolve(1, 6, "A");
        Assert.Equal(2, world.SignerSubjectCallCount);
    }

    [Fact]
    public void Normalize_CaseAndVersionDirectories_SameAppKeyPath()
    {
        var a = AppKeyImagePath.Normalize(@"C:\Apps\1.2.3\App.EXE");
        var b = AppKeyImagePath.Normalize(@"c:\apps\v1.0.0\app.exe");
        var c = AppKeyImagePath.Normalize(@"c:\apps\app.exe");
        Assert.Equal(@"c:\apps\app.exe", a);
        Assert.Equal(a, b);
        Assert.Equal(a, c);
    }

    [Fact]
    public void Normalize_ShortPathExpansion_SameAsLongPath()
    {
        var from83 = AppKeyImagePath.Normalize(
            @"c:\progra~1\app\1.2.3\app.exe",
            _ => @"C:\Program Files\app\1.2.3\app.exe");
        var fromLong = AppKeyImagePath.Normalize(@"C:\Program Files\App\APP.EXE");
        Assert.Equal(@"c:\program files\app\app.exe", from83);
        Assert.Equal(from83, fromLong);
    }

    [Fact]
    public void HasStableIdentity_RejectsClassNameOnly()
    {
        var identity = new AppIdentity(null, null, null, null, null, "#32770");
        Assert.False(AppKeyResolver.HasStableIdentity(identity));
        Assert.Equal("|#32770", AppKeyResolver.Compute(identity).Value);
    }

    [Fact]
    public void ProductionEntrypoints_DoNotHandFillNullAppIdentity()
    {
        var tools = FindSrc("Mcp/ComputerUseTools.cs");
        var observe = FindSrc("Services/ObserveService.cs");
        var click = FindSrc("Services/ClickControlService.cs");
        Assert.DoesNotContain("new AppIdentity(null, null, null, null", File.ReadAllText(tools), StringComparison.Ordinal);
        Assert.DoesNotContain("new AppIdentity(null, null, null, null", File.ReadAllText(observe), StringComparison.Ordinal);
        Assert.DoesNotContain("new AppIdentity(null, null, null, null", File.ReadAllText(click), StringComparison.Ordinal);
        Assert.Contains("AppIdentityFactory", File.ReadAllText(tools), StringComparison.Ordinal);
        Assert.Contains("_identities.Resolve", File.ReadAllText(observe), StringComparison.Ordinal);
        Assert.Contains("_identities.Resolve", File.ReadAllText(click), StringComparison.Ordinal);
    }

    private static string FindSrc(string relativeUnderMcp)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "ComputerUse.Mcp", relativeUnderMcp);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(relativeUnderMcp);
    }
}

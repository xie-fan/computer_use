using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Identity;
using ComputerUse.Mcp.Memory;

namespace ComputerUse.Mcp.Tests.Memory;

public sealed class MemoryCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cu-mem-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void PutAndList_RoundTripIdsWithoutImages()
    {
        var catalog = new MemoryCatalog(_root, Limits.V1);
        var screenId = catalog.PutScreen("app.a", "home", fingerprintCount: 2);
        var controlId = catalog.PutControl("app.a", screenId, "start");

        var listed = catalog.List("app.a");
        Assert.Contains(listed, s => s.ScreenId == screenId && s.ScreenKey == "home" && s.FingerprintCount == 2);
        Assert.Contains(listed.SelectMany(s => s.Controls), c => c.ControlId == controlId && c.Name == "start");
    }

    [Fact]
    public void ForgetScreen_RemovesControls()
    {
        var catalog = new MemoryCatalog(_root, Limits.V1);
        var screenId = catalog.PutScreen("app.a", "home", 2);
        catalog.PutControl("app.a", screenId, "start");
        catalog.ForgetScreen("app.a", screenId);
        Assert.DoesNotContain(catalog.List("app.a"), s => s.ScreenId == screenId);
    }

    [Fact]
    public void ExceedsScreenQuota_Rejects()
    {
        var limits = Limits.V1 with { MaxScreensPerAppKey = 2 };
        var catalog = new MemoryCatalog(_root, limits);
        catalog.PutScreen("app.a", "s1", 2);
        catalog.PutScreen("app.a", "s2", 2);
        Assert.ThrowsAny<Exception>(() => catalog.PutScreen("app.a", "s3", 2));
    }

    [Fact]
    public void ExceedsControlQuota_Rejects()
    {
        var limits = Limits.V1 with { MaxControlsPerScreen = 2 };
        var catalog = new MemoryCatalog(_root, limits);
        var screenId = catalog.PutScreen("app.a", "home", 2);
        catalog.PutControl("app.a", screenId, "a");
        catalog.PutControl("app.a", screenId, "b");
        Assert.ThrowsAny<Exception>(() => catalog.PutControl("app.a", screenId, "c"));
    }

    [Fact]
    public void ForgetScreen_DotOrDotDot_DoesNotDeleteExistingScreens()
    {
        var catalog = new MemoryCatalog(_root, Limits.V1);
        var first = catalog.PutScreen("app.a", "s1", 2);
        var second = catalog.PutScreen("app.a", "s2", 2);

        catalog.ForgetScreen("app.a", "..");
        catalog.ForgetScreen("app.a", ".");
        catalog.ForgetScreen("app.a", Path.Combine("..", first));

        var listed = catalog.List("app.a");
        Assert.Contains(listed, s => s.ScreenId == first && s.ScreenKey == "s1");
        Assert.Contains(listed, s => s.ScreenId == second && s.ScreenKey == "s2");
        Assert.Equal(2, listed.Count);
    }

    [Fact]
    public void ForgetScreen_EllipsisOrTrailingDots_DoesNotDeleteExistingScreens()
    {
        var catalog = new MemoryCatalog(_root, Limits.V1);
        var first = catalog.PutScreen("app.a", "s1", 2);
        var second = catalog.PutScreen("app.a", "s2", 2);

        catalog.ForgetScreen("app.a", "...");
        catalog.ForgetScreen("app.a", "....");
        catalog.ForgetScreen("app.a", "foo..");
        catalog.ForgetScreen("app.a", first + "..");

        var listed = catalog.List("app.a");
        Assert.Contains(listed, s => s.ScreenId == first && s.ScreenKey == "s1");
        Assert.Contains(listed, s => s.ScreenId == second && s.ScreenKey == "s2");
        Assert.Equal(2, listed.Count);
    }

    [Fact]
    public void ListDoesNotReturnPixelPayloads()
    {
        var catalog = new MemoryCatalog(_root, Limits.V1);
        var screenId = catalog.PutScreen("app.a", "home", 2);
        catalog.PutControl("app.a", screenId, "start");
        var json = System.Text.Json.JsonSerializer.Serialize(catalog.List("app.a"));
        Assert.DoesNotContain("data:image", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"png\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bgra", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PutScreen_WritesIdentityDiagnosticsToAppJson()
    {
        var catalog = new MemoryCatalog(_root, Limits.V1);
        var identity = new AppIdentity(
            "Pfn.Name_id",
            "CN=Signer",
            "Product",
            "1.2.3",
            @"c:\apps\normalized\app.exe",
            "Notepad")
        {
            RawImagePath = @"C:\APPS\app.exe"
        };
        catalog.PutScreen("app.key", "home", 2, identity);

        Assert.True(catalog.TryGetAppMetadata("app.key", out var meta));
        Assert.Equal("app.key", meta.AppKey);
        Assert.Equal("Pfn.Name_id", meta.PackageFamilyName);
        Assert.Equal("CN=Signer", meta.SignerSubject);
        Assert.Equal("Product", meta.ProductName);
        Assert.Equal("1.2.3", meta.ProductVersion);
        Assert.Equal(@"C:\APPS\app.exe", meta.ImagePath);
        Assert.Equal("Notepad", meta.ClassName);
    }

    [Fact]
    public void OldAppJson_WithoutDiagnostics_StillLists()
    {
        var catalog = new MemoryCatalog(_root, Limits.V1);
        var screenId = catalog.PutScreen("app.a", "home", 2);
        Assert.True(catalog.TryGetAppMetadata("app.a", out var meta));
        Assert.Equal("app.a", meta.AppKey);
        Assert.Null(meta.PackageFamilyName);
        Assert.Null(meta.ImagePath);
        Assert.Contains(catalog.List("app.a"), s => s.ScreenId == screenId);
    }

    [Fact]
    public void DiagnosticsDoNotChangeDirectoryHash()
    {
        var a = Path.Combine(_root, "a");
        var b = Path.Combine(_root, "b");
        var catA = new MemoryCatalog(a, Limits.V1);
        var catB = new MemoryCatalog(b, Limits.V1);
        catA.PutScreen("same-key", "s", 2);
        catB.PutScreen(
            "same-key",
            "s",
            2,
            new AppIdentity("pfn", "CN=X", "P", "1", @"c:\x.exe", "Cls"));

        var dirsA = Directory.GetDirectories(a);
        var dirsB = Directory.GetDirectories(b);
        Assert.Single(dirsA);
        Assert.Single(dirsB);
        Assert.Equal(Path.GetFileName(dirsA[0]), Path.GetFileName(dirsB[0]));
    }
}

using ComputerUse.Mcp.Identity;

namespace ComputerUse.Mcp.Tests;

public sealed class AppKeyResolverTests
{
    [Fact]
    public void Msix_UsesPfnAndClassName()
    {
        var key = AppKeyResolver.Compute(new AppIdentity(
            PackageFamilyName: "Contoso.App_8wekyb3d8bbwe",
            SignerSubject: "CN=Contoso",
            ProductName: "App",
            ProductVersion: "1.0.0",
            NormalizedImagePath: @"c:\program files\windowsapps\contoso.app\app.exe",
            ClassName: "WinUI"));

        Assert.Contains("Contoso.App_8wekyb3d8bbwe", key.Value, StringComparison.Ordinal);
        Assert.Contains("WinUI", key.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("windowsapps", key.Value, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Contoso.App_8wekyb3d8bbwe", key.Diagnostics.PackageFamilyName);
        Assert.Equal(@"c:\program files\windowsapps\contoso.app\app.exe", key.Diagnostics.NormalizedImagePath);
    }

    [Fact]
    public void SignedWin32_UsesSignerProductVersionClass()
    {
        var key = AppKeyResolver.Compute(new AppIdentity(
            PackageFamilyName: null,
            SignerSubject: "CN=Example Corp",
            ProductName: "Editor",
            ProductVersion: "2.1.0",
            NormalizedImagePath: @"c:\apps\editor\editor.exe",
            ClassName: "MainWnd"));

        Assert.Contains("CN=Example Corp", key.Value, StringComparison.Ordinal);
        Assert.Contains("Editor", key.Value, StringComparison.Ordinal);
        Assert.Contains("2.1.0", key.Value, StringComparison.Ordinal);
        Assert.Contains("MainWnd", key.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("editor.exe", key.Value, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(@"c:\apps\editor\editor.exe", key.Diagnostics.NormalizedImagePath);
        Assert.Equal("CN=Example Corp", key.Diagnostics.SignerSubject);
    }

    [Fact]
    public void UnsignedWithVersion_IncludesNormalizedPath()
    {
        var key = AppKeyResolver.Compute(new AppIdentity(
            PackageFamilyName: null,
            SignerSubject: null,
            ProductName: "Tool",
            ProductVersion: "3.0.0",
            NormalizedImagePath: @"c:\tools\tool.exe",
            ClassName: "ToolWnd"));

        Assert.Contains("Tool", key.Value, StringComparison.Ordinal);
        Assert.Contains("3.0.0", key.Value, StringComparison.Ordinal);
        Assert.Contains(@"c:\tools\tool.exe", key.Value, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ToolWnd", key.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Fallback_NormalizedPathAndClass()
    {
        var key = AppKeyResolver.Compute(new AppIdentity(
            PackageFamilyName: null,
            SignerSubject: null,
            ProductName: null,
            ProductVersion: null,
            NormalizedImagePath: @"c:\bin\app.exe",
            ClassName: "AppClass"));

        Assert.Contains(@"c:\bin\app.exe", key.Value, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AppClass", key.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void DifferentClassName_DoesNotMerge()
    {
        var path = @"c:\apps\app.exe";
        var a = AppKeyResolver.Compute(new AppIdentity(null, null, null, null, path, "ClassA"));
        var b = AppKeyResolver.Compute(new AppIdentity(null, null, null, null, path, "ClassB"));
        Assert.NotEqual(a.Value, b.Value);
    }

    [Fact]
    public void DiagnosticsRetainRawFields()
    {
        var identity = new AppIdentity(
            "Pfn.Name_id",
            "CN=Signer",
            "Product",
            "9.9.9",
            @"c:\raw\path.exe",
            "Cls");
        var key = AppKeyResolver.Compute(identity);
        Assert.Equal("Pfn.Name_id", key.Diagnostics.PackageFamilyName);
        Assert.Equal("CN=Signer", key.Diagnostics.SignerSubject);
        Assert.Equal("Product", key.Diagnostics.ProductName);
        Assert.Equal("9.9.9", key.Diagnostics.ProductVersion);
        Assert.Equal(@"c:\raw\path.exe", key.Diagnostics.NormalizedImagePath);
        Assert.Equal("Cls", key.Diagnostics.ClassName);
    }
}

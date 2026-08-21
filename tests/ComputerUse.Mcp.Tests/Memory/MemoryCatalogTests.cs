using ComputerUse.Mcp.Domain;
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
}

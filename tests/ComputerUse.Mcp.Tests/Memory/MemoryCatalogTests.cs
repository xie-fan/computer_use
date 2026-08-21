using ComputerUse.Mcp.Capture;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Identity;
using ComputerUse.Mcp.Memory;
using ComputerUse.Mcp.Tests.Support;
using System.Text.Json;

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

    [Fact]
    public void LoadAppScreens_DoesNotDecodeControlPixels()
    {
        var catalog = new MemoryCatalog(_root, Limits.V1);
        var png = TinyPng();
        var screenId = catalog.PutScreen("app.a", "home", [Fp(png)], Snap());
        var controlId = catalog.PutControl("app.a", screenId, "go", Ctrl(png));

        var loaded = catalog.LoadAppScreens("app.a");
        Assert.Contains(loaded, s => s.ScreenId == screenId);
        Assert.Contains(loaded.SelectMany(s => s.Controls), c => c.ControlId == controlId && c.Bgra is null);

        Assert.True(catalog.TryLoadControl("app.a", controlId, out var control));
        Assert.NotNull(control.Bgra);
        Assert.Contains(catalog.LoadScreenControls("app.a", screenId), c => c.Bgra is not null);
    }

    [Fact]
    public void LoadAppScreens_NominatesWithoutMissingPeerControlPng()
    {
        var catalog = new MemoryCatalog(_root, Limits.V1);
        var png = TinyPng();
        var screenA = catalog.PutScreen("app.a", "a", [Fp(png)], Snap(bits: 1));
        catalog.PutControl("app.a", screenA, "go-a", Ctrl(png));
        var screenB = catalog.PutScreen("app.a", "b", [Fp(png)], Snap(bits: 2));
        var controlB = catalog.PutControl("app.a", screenB, "go-b", Ctrl(png));

        var pngB = Directory.GetFiles(_root, controlB + ".png", SearchOption.AllDirectories).Single();
        File.Delete(pngB);

        var loaded = catalog.LoadAppScreens("app.a");
        Assert.Equal(2, loaded.Count);
        Assert.All(loaded.SelectMany(s => s.Controls), c => Assert.Null(c.Bgra));
        Assert.Contains(loaded, s => s.ScreenId == screenA && s.Fingerprints.Count == 1);
        Assert.Contains(loaded, s => s.ScreenId == screenB && s.Fingerprints.Count == 1);

        var hydratedB = catalog.LoadScreenControls("app.a", screenB);
        Assert.Contains(hydratedB, c => c.ControlId == controlB && c.Bgra is null);
    }

    [Fact]
    public void LibraryQuota_UsesRunningCounterNotFullRescan()
    {
        var png = TinyPng();
        var limits = Limits.V1 with { MaxMemoryLibraryBytes = 80_000 };
        var catalog = new MemoryCatalog(_root, limits);
        var screenId = catalog.PutScreen("app.a", "home", [Fp(png)], Snap());
        catalog.PutControl("app.a", screenId, "a", Ctrl(png));

        File.WriteAllBytes(Path.Combine(_root, "noise.bin"), new byte[1024 * 1024]);

        var second = catalog.PutControl("app.a", screenId, "b", Ctrl(png));
        Assert.False(string.IsNullOrWhiteSpace(second));
    }

    [Fact]
    public void LibraryQuota_RejectsWhenRunningCounterExceedsLimit()
    {
        var png = TinyPng();
        var limits = Limits.V1 with { MaxMemoryLibraryBytes = png.Length };
        var catalog = new MemoryCatalog(_root, limits);
        var ex = Assert.Throws<ComputerUseException>(() =>
            catalog.PutScreen("app.a", "home", [Fp(png)], Snap()));
        Assert.Equal(ErrorCodes.PayloadTooLarge, ex.Code);
    }

    [Fact]
    public void ForgetScreen_FreesRunningCounter()
    {
        var png = TinyPng();
        var measureRoot = Path.Combine(_root, "m");
        var measure = new MemoryCatalog(measureRoot, Limits.V1);
        measure.PutScreen("app.a", "home", [Fp(png)], Snap());
        var used = Directory.EnumerateFiles(measureRoot, "*", SearchOption.AllDirectories)
            .Sum(f => new FileInfo(f).Length);

        var limits = Limits.V1 with { MaxMemoryLibraryBytes = (int)used + png.Length / 2 };
        var liveRoot = Path.Combine(_root, "c");
        var catalog = new MemoryCatalog(liveRoot, limits);
        var screenId = catalog.PutScreen("app.a", "home", [Fp(png)], Snap());
        var overflow = Assert.Throws<ComputerUseException>(() =>
            catalog.PutScreen("app.a", "other", [Fp(png)], Snap()));
        Assert.Equal(ErrorCodes.PayloadTooLarge, overflow.Code);

        catalog.ForgetScreen("app.a", screenId);
        var again = catalog.PutScreen("app.a", "other", [Fp(png)], Snap());
        Assert.False(string.IsNullOrWhiteSpace(again));
    }

    [Fact]
    public void RememberAfterSoftTtl_EvictsUnmatchedScreen()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var catalog = new MemoryCatalog(_root, Limits.V1, () => now);
        var oldId = catalog.PutScreen("app.a", "old", 2);
        Assert.Contains(catalog.List("app.a"), s => s.ScreenId == oldId);

        now = now.AddDays(31);
        Assert.Contains(catalog.List("app.a"), s => s.ScreenId == oldId);

        var newId = catalog.PutScreen("app.a", "new", 2);
        var listed = catalog.List("app.a");
        Assert.DoesNotContain(listed, s => s.ScreenId == oldId);
        Assert.Contains(listed, s => s.ScreenId == newId);
    }

    [Fact]
    public void SoftTtl_UsesLastMatchedAtNotCreatedAt()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var catalog = new MemoryCatalog(_root, Limits.V1, () => now);
        var png = TinyPng();
        var kept = catalog.PutScreen("app.a", "kept", [Fp(png)], Snap(bits: 1));
        var dropped = catalog.PutScreen("app.a", "dropped", [Fp(png)], Snap(bits: 2));
        var controlId = catalog.PutControl("app.a", kept, "go", Ctrl(png));

        now = now.AddDays(2);
        catalog.TouchMatch("app.a", kept, controlId);
        now = now.AddDays(29);

        catalog.PutScreen("app.a", "fresh", 2);
        var listed = catalog.List("app.a");
        Assert.Contains(listed, s => s.ScreenId == kept);
        Assert.DoesNotContain(listed, s => s.ScreenId == dropped);
    }

    [Fact]
    public void PutControl_SameName_OverwritesAndKeepsId()
    {
        var png = TinyPng();
        var catalog = new MemoryCatalog(_root, Limits.V1);
        var screenId = catalog.PutScreen("app.a", "home", [Fp(png)], Snap());
        var first = catalog.PutControl("app.a", screenId, "go", Ctrl(png));
        var second = catalog.PutControl("app.a", screenId, "go", Ctrl(png));
        Assert.Equal(first, second);
        var listed = catalog.List("app.a");
        Assert.Single(listed);
        Assert.Single(listed[0].Controls);
        Assert.Equal(first, listed[0].Controls[0].ControlId);
    }

    [Fact]
    public void PutControl_SameName_DoesNotConsumeControlQuota()
    {
        var limits = Limits.V1 with { MaxControlsPerScreen = 1 };
        var catalog = new MemoryCatalog(_root, limits);
        var screenId = catalog.PutScreen("app.a", "home", 2);
        var first = catalog.PutControl("app.a", screenId, "go");
        var again = catalog.PutControl("app.a", screenId, "go");
        Assert.Equal(first, again);
        Assert.ThrowsAny<Exception>(() => catalog.PutControl("app.a", screenId, "other"));
        Assert.Single(Assert.Single(catalog.List("app.a")).Controls);
    }

    [Fact]
    public async Task ParallelPutScreen_SameRoot_JsonReadableAndWithinQuota()
    {
        var png = TinyPng();
        var limits = Limits.V1 with { MaxScreensPerAppKey = 1 };
        var catA = new MemoryCatalog(_root, limits);
        var catB = new MemoryCatalog(_root, limits);
        Exception? exA = null;
        Exception? exB = null;
        await Task.WhenAll(
            Task.Run(() =>
            {
                try
                {
                    catA.PutScreen("app.a", "s1", [Fp(png)], Snap(bits: 1));
                }
                catch (Exception ex)
                {
                    exA = ex;
                }
            }),
            Task.Run(() =>
            {
                try
                {
                    catB.PutScreen("app.a", "s2", [Fp(png)], Snap(bits: 2));
                }
                catch (Exception ex)
                {
                    exB = ex;
                }
            }));

        Assert.True((exA is null) ^ (exB is null));
        var failed = (exA ?? exB) as ComputerUseException;
        Assert.NotNull(failed);
        Assert.Equal(ErrorCodes.PayloadTooLarge, failed!.Code);

        foreach (var json in Directory.GetFiles(_root, "screen.json", SearchOption.AllDirectories))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(json));
            Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
            Assert.True(doc.RootElement.TryGetProperty("screenId", out _));
            Assert.True(doc.RootElement.TryGetProperty("fingerprints", out var fingerprints));
            Assert.True(fingerprints.GetArrayLength() >= 1);
        }

        Assert.Single(new MemoryCatalog(_root, limits).List("app.a"));
    }

    [Fact]
    public void CreatesMemoryRootDirectory()
    {
        var catalog = new MemoryCatalog(_root, Limits.V1);
        Assert.True(Directory.Exists(_root));
        Assert.NotNull(catalog);
    }

    private static byte[] TinyPng() =>
        PngCodec.EncodeBgra(BgraFrames.Checker(24, 24, 2), 24, 24, 96);

    private static FingerprintAsset Fp(byte[] png) =>
        new(0, 0, 24, 24, png, 0, 0, 0.1, 0.1);

    private static ControlAsset Ctrl(byte[] png) =>
        new(png, 24, 24, 0.1, 0.1, 0.2, 0.2, 100, 100, 96, 96);

    private static ScreenSnapshot Snap(ulong bits = 1) =>
        new(100, 100, 100, 100, 96, 96, bits);
}

using ComputerUse.Mcp.Tests.Support;
using ComputerUse.Mcp.Vision;

namespace ComputerUse.Mcp.Tests.Vision;

public sealed class ScreenIdentifierTests
{
    private const int Size = 160;
    private const int Patch = 32;
    private const int ContentX = 48;
    private const int ContentY = 72;

    [Fact]
    public void SyntheticScreenB_ReturnsOnlyB()
    {
        var frameA = ScreenA();
        var frameB = ScreenB();
        var library = new[] { Entry("screen-a", frameA, "ctrl-a"), Entry("screen-b", frameB, "ctrl-b") };

        var result = ScreenIdentifier.Identify(frameB, Size, Size, Size * 4, library);

        Assert.Equal(ScreenIdentifyStatus.Identified, result.Status);
        Assert.Equal("screen-b", result.ScreenId);
    }

    [Fact]
    public void NoCandidate_IsUnknown()
    {
        var library = new[] { Entry("screen-a", ScreenA(), "ctrl-a") };
        var other = BgraFrames.Noise(Size, Size, seed: 99);
        var result = ScreenIdentifier.Identify(other, Size, Size, Size * 4, library);
        Assert.Equal(ScreenIdentifyStatus.Unknown, result.Status);
        Assert.Null(result.ScreenId);
    }

    [Fact]
    public void TwoTiedCandidates_IsAmbiguous()
    {
        var frameB = ScreenB();
        var library = new[]
        {
            Entry("screen-b1", frameB, "ctrl-1"),
            Entry("screen-b2", frameB, "ctrl-2")
        };
        var result = ScreenIdentifier.Identify(frameB, Size, Size, Size * 4, library);
        Assert.Equal(ScreenIdentifyStatus.Ambiguous, result.Status);
        Assert.Null(result.ScreenId);
    }

    [Fact]
    public void ControlFromScreenA_OnFrameB_IsMismatch()
    {
        var library = new[] { Entry("screen-a", ScreenA(), "ctrl-a"), Entry("screen-b", ScreenB(), "ctrl-b") };
        var result = ScreenIdentifier.Identify(ScreenB(), Size, Size, Size * 4, library, requiredScreenId: "screen-a");
        Assert.Equal(ScreenIdentifyStatus.Mismatch, result.Status);
    }

    [Fact]
    public void SharedChromeDifferentContent_IsNotIdentifiedAsA()
    {
        var frameA = ChromeWithContent(contentSeed: 3);
        var frameB = ChromeWithContent(contentSeed: 91);
        var library = new[]
        {
            ChromeEntry("screen-a", frameA),
            ChromeEntry("screen-b", frameB)
        };

        var result = ScreenIdentifier.Identify(frameB, Size, Size, Size * 4, library);

        Assert.NotEqual("screen-a", result.ScreenId);
        if (result.Status == ScreenIdentifyStatus.Identified)
            Assert.Equal("screen-b", result.ScreenId);
    }

    [Fact]
    public void SharedChromeLibraryA_OnFrameB_IsUnknown()
    {
        var frameA = ChromeWithContent(contentSeed: 3);
        var frameB = ChromeWithContent(contentSeed: 91);
        var library = new[] { ChromeEntry("screen-a", frameA) };

        var result = ScreenIdentifier.Identify(frameB, Size, Size, Size * 4, library);

        Assert.Equal(ScreenIdentifyStatus.Unknown, result.Status);
        Assert.Null(result.ScreenId);

        var mismatch = ScreenIdentifier.Identify(frameB, Size, Size, Size * 4, library, requiredScreenId: "screen-a");
        Assert.Equal(ScreenIdentifyStatus.Mismatch, mismatch.Status);
    }

    [Fact]
    public void CatalogNormalizedBox_IsUsedInsteadOfPixelOverCurrentWidth()
    {
        var frame = ScreenA();
        var hash = PerceptualHash.Compute(frame, Size, Size, Size * 4);
        // 像素框假装来自 2× 宽的入库帧（X=80），当前帧上补丁仍在 8,8；入库 Nx 按当前视觉位置。
        var library = new[]
        {
            new StoredScreenCatalogEntry(
                "screen-a",
                hash,
                [
                    Fingerprint(frame, pixelX: 80, pixelY: 80, visualX: 8, visualY: 8),
                    Fingerprint(frame, pixelX: 240, pixelY: 240, visualX: 120, visualY: 120)
                ],
                [ControlFrom(frame, "ctrl-a", 8, 8, Patch, Patch)])
        };

        var result = ScreenIdentifier.Identify(frame, Size, Size, Size * 4, library);

        Assert.Equal(ScreenIdentifyStatus.Identified, result.Status);
        Assert.Equal("screen-a", result.ScreenId);
    }

    [Fact]
    public void ScrambledControlRelativeToFingerprint_IsNotIdentified()
    {
        var frame = ChromeWithContent(contentSeed: 3);
        var real = ControlFrom(frame, "ctrl", ContentX, ContentY, Patch, Patch);
        var scrambled = real with { Nx = 0.70, Ny = 0.05 };
        var library = new[] { ChromeEntry("screen-a", frame, scrambled) };

        var result = ScreenIdentifier.Identify(frame, Size, Size, Size * 4, library);

        Assert.NotEqual(ScreenIdentifyStatus.Identified, result.Status);
    }

    [Fact]
    public void DifferentNoiseSeedLookalike_IsNotIdentified()
    {
        var remembered = ScreenA();
        var lookalike = BgraFrames.Solid(Size, Size, 30, 30, 30);
        BgraFrames.Paste(lookalike, Size, BgraFrames.Checker(Patch, Patch, 2), Patch, Patch, 8, 8);
        BgraFrames.Paste(lookalike, Size, BgraFrames.Noise(Patch, Patch, 91), Patch, Patch, 120, 120);
        var library = new[] { Entry("screen-a", remembered, "ctrl-a") };

        var result = ScreenIdentifier.Identify(lookalike, Size, Size, Size * 4, library);

        Assert.NotEqual(ScreenIdentifyStatus.Identified, result.Status);
    }

    [Fact]
    public void Identify_LoadsControlPixelsOnlyForNominatedScreens()
    {
        var frameA = ScreenA();
        var originals = new Dictionary<string, IReadOnlyList<StoredControlLayout>>(StringComparer.Ordinal)
        {
            ["screen-a"] = [ControlFrom(frameA, "ctrl-a", 8, 8, Patch, Patch)],
            ["screen-b"] = [ControlFrom(ScreenB(), "ctrl-b", 8, 8, Patch, Patch)],
            ["screen-c"] = [ControlFrom(BgraFrames.Noise(Size, Size, 201), "ctrl-c", 8, 8, Patch, Patch)],
            ["screen-d"] = [ControlFrom(BgraFrames.Noise(Size, Size, 401), "ctrl-d", 8, 8, Patch, Patch)]
        };
        var library = new[]
        {
            StripControlPixels(Entry("screen-a", frameA, "ctrl-a")),
            StripControlPixels(Entry("screen-b", ScreenB(), "ctrl-b")),
            StripControlPixels(NoiseEntry("screen-c", 201)),
            StripControlPixels(NoiseEntry("screen-d", 401))
        };
        var loaded = new HashSet<string>(StringComparer.Ordinal);

        var result = ScreenIdentifier.Identify(
            frameA,
            Size,
            Size,
            Size * 4,
            library,
            loadNominatedControls: id =>
            {
                loaded.Add(id);
                return originals[id];
            });

        Assert.Equal(ScreenIdentifyStatus.Identified, result.Status);
        Assert.Equal("screen-a", result.ScreenId);
        Assert.Contains("screen-a", loaded);
        Assert.True(loaded.Count <= 3);
        Assert.Equal(3, loaded.Count);
        Assert.Single(originals.Keys.Except(loaded));
    }

    private static StoredScreenCatalogEntry StripControlPixels(StoredScreenCatalogEntry entry)
    {
        var stripped = new StoredControlLayout[entry.Controls.Count];
        for (var i = 0; i < entry.Controls.Count; i++)
        {
            var control = entry.Controls[i];
            stripped[i] = control with { Bgra = null };
        }

        return entry with { Controls = stripped };
    }

    private static StoredScreenCatalogEntry NoiseEntry(string screenId, int seed)
    {
        var frame = BgraFrames.Noise(Size, Size, seed);
        var hash = PerceptualHash.Compute(frame, Size, Size, Size * 4);
        return new StoredScreenCatalogEntry(
            screenId,
            hash,
            [
                Fingerprint(frame, 8, 8),
                Fingerprint(frame, 120, 120)
            ],
            [ControlFrom(frame, "ctrl", 8, 8, Patch, Patch)]);
    }

    private static byte[] ScreenA()
    {
        var frame = BgraFrames.Solid(Size, Size, 30, 30, 30);
        BgraFrames.Paste(frame, Size, BgraFrames.Checker(Patch, Patch, 2), Patch, Patch, 8, 8);
        BgraFrames.Paste(frame, Size, BgraFrames.Noise(Patch, Patch, 3), Patch, Patch, 120, 120);
        return frame;
    }

    private static byte[] ScreenB()
    {
        var frame = BgraFrames.Solid(Size, Size, 30, 30, 30);
        BgraFrames.Paste(frame, Size, BgraFrames.Checker(Patch, Patch, 5), Patch, Patch, 8, 8);
        BgraFrames.Paste(frame, Size, BgraFrames.Noise(Patch, Patch, 91), Patch, Patch, 120, 120);
        return frame;
    }

    private static byte[] ChromeWithContent(int contentSeed)
    {
        var frame = BgraFrames.Solid(Size, Size, 30, 30, 30);
        BgraFrames.Paste(frame, Size, BgraFrames.Checker(Patch, Patch, 2), Patch, Patch, 8, 8);
        BgraFrames.Paste(frame, Size, BgraFrames.Checker(Patch, Patch, 3), Patch, Patch, 120, 8);
        BgraFrames.Paste(frame, Size, BgraFrames.Noise(64, 64, contentSeed), 64, 64, ContentX, ContentY);
        return frame;
    }

    private static StoredScreenCatalogEntry Entry(string screenId, byte[] frame, string controlId)
    {
        var hash = PerceptualHash.Compute(frame, Size, Size, Size * 4);
        return new StoredScreenCatalogEntry(
            screenId,
            hash,
            [
                Fingerprint(frame, 8, 8),
                Fingerprint(frame, 120, 120)
            ],
            [ControlFrom(frame, controlId, 8, 8, Patch, Patch)]);
    }

    private static StoredScreenCatalogEntry ChromeEntry(
        string screenId,
        byte[] frame,
        StoredControlLayout? control = null)
    {
        var hash = PerceptualHash.Compute(frame, Size, Size, Size * 4);
        return new StoredScreenCatalogEntry(
            screenId,
            hash,
            [
                Fingerprint(frame, 8, 8),
                Fingerprint(frame, 120, 8)
            ],
            [control ?? ControlFrom(frame, "ctrl", ContentX, ContentY, Patch, Patch)]);
    }

    private static StoredControlLayout ControlFrom(byte[] frame, string id, int x, int y, int w, int h) =>
        new(
            id,
            x / (double)Size,
            y / (double)Size,
            w / (double)Size,
            h / (double)Size,
            w,
            h,
            BgraFrames.Crop(frame, Size, x, y, w, h));

    private static ScreenFingerprint Fingerprint(byte[] frame, int x, int y) =>
        Fingerprint(frame, pixelX: x, pixelY: y, visualX: x, visualY: y);

    private static ScreenFingerprint Fingerprint(
        byte[] frame,
        int pixelX,
        int pixelY,
        int visualX,
        int visualY)
    {
        var crop = BgraFrames.Crop(frame, Size, visualX, visualY, Patch, Patch);
        return new ScreenFingerprint(
            pixelX,
            pixelY,
            Patch,
            Patch,
            visualX / (double)Size,
            visualY / (double)Size,
            Patch / (double)Size,
            Patch / (double)Size,
            crop);
    }
}

using ComputerUse.Mcp.Tests.Support;
using ComputerUse.Mcp.Vision;

namespace ComputerUse.Mcp.Tests.Vision;

public sealed class ScreenIdentifierTests
{
    private const int Size = 160;
    private const int Patch = 32;

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

    private static StoredScreenCatalogEntry Entry(string screenId, byte[] frame, string controlId)
    {
        var fp1 = BgraFrames.Crop(frame, Size, 8, 8, Patch, Patch);
        var fp2 = BgraFrames.Crop(frame, Size, 120, 120, Patch, Patch);
        var hash = PerceptualHash.Compute(frame, Size, Size, Size * 4);
        return new StoredScreenCatalogEntry(
            screenId,
            hash,
            [
                new ScreenFingerprint(8, 8, Patch, Patch, fp1),
                new ScreenFingerprint(120, 120, Patch, Patch, fp2)
            ],
            [new StoredControlLayout(controlId, 0.1, 0.1, 0.2, 0.2)]);
    }
}

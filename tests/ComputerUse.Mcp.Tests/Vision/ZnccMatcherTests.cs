using System.Diagnostics;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Tests.Support;
using ComputerUse.Mcp.Vision;

namespace ComputerUse.Mcp.Tests.Vision;

public sealed class ZnccMatcherTests
{
    [Fact]
    public void UniqueHighContrastPatch_FindsExpectedOrigin()
    {
        var hay = BgraFrames.Solid(64, 64, 0, 0, 0);
        var tmpl = BgraFrames.Checker(16, 16, cell: 2);
        Paste(hay, 64, tmpl, 16, 16, 10, 12);

        var result = ZnccMatcher.Match(
            hay, 64, 64, 256,
            tmpl, 16, 16, 64,
            Limits.V1.TemplateScaleMin,
            Limits.V1.TemplateScaleMax);

        Assert.Equal(TemplateMatchStatus.Found, result.Status);
        Assert.InRange(result.X, 9, 11);
        Assert.InRange(result.Y, 11, 13);
    }

    [Fact]
    public void TwoEquallyGoodMatches_IsTemplateAmbiguous()
    {
        var hay = BgraFrames.Solid(80, 40, 0, 0, 0);
        var tmpl = BgraFrames.Checker(12, 12, cell: 2);
        Paste(hay, 80, tmpl, 12, 12, 4, 4);
        Paste(hay, 80, tmpl, 12, 12, 50, 4);

        var result = ZnccMatcher.Match(
            hay, 80, 40, 320,
            tmpl, 12, 12, 48,
            Limits.V1.TemplateScaleMin,
            Limits.V1.TemplateScaleMax);

        Assert.Equal(TemplateMatchStatus.Ambiguous, result.Status);
    }

    [Fact]
    public void MissingPatch_IsTemplateNotFound()
    {
        var hay = BgraFrames.Solid(48, 48, 0, 0, 0);
        var tmpl = BgraFrames.Checker(12, 12);

        var result = ZnccMatcher.Match(
            hay, 48, 48, 192,
            tmpl, 12, 12, 48,
            Limits.V1.TemplateScaleMin,
            Limits.V1.TemplateScaleMax);

        Assert.Equal(TemplateMatchStatus.NotFound, result.Status);
    }

    [Fact]
    public void RequestedScaleOutsidePyramid_IsTemplateScaleMismatch()
    {
        var hay = BgraFrames.Noise(32, 32);
        var tmpl = BgraFrames.Checker(12, 12);
        var result = ZnccMatcher.Match(
            hay, 32, 32, 128,
            tmpl, 12, 12, 48,
            minScale: 2.0,
            maxScale: 2.2);

        Assert.Equal(TemplateMatchStatus.ScaleMismatch, result.Status);
    }

    [Fact]
    public void CancelledToken_ReturnsNotFoundQuickly()
    {
        var hay = BgraFrames.Solid(1280, 720, 0, 0, 0);
        var tmpl = BgraFrames.Checker(64, 64, cell: 2);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sw = Stopwatch.StartNew();
        var result = ZnccMatcher.Match(
            hay, 1280, 720, 1280 * 4,
            tmpl, 64, 64, 64 * 4,
            Limits.V1.TemplateScaleMin,
            Limits.V1.TemplateScaleMax,
            cts.Token);
        sw.Stop();

        Assert.Equal(TemplateMatchStatus.NotFound, result.Status);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), sw.Elapsed.ToString());
    }

    [Fact]
    public void LargeTemplateOnHdHaystack_SkipsFullFrameFallback()
    {
        Assert.True(ZnccMatcher.ShouldSkipFullFrameFallback(64, 64, 1280, 720));
        Assert.True(ZnccMatcher.ShouldSkipFullFrameFallback(256, 256, 1280, 720));
        Assert.False(ZnccMatcher.ShouldSkipFullFrameFallback(32, 32, 1280, 720));
        Assert.False(ZnccMatcher.ShouldSkipFullFrameFallback(64, 64, 100, 80));
        Assert.False(ZnccMatcher.ShouldSkipFullFrameFallback(12, 12, 48, 48));
    }

    [Fact]
    public void SearchTimeoutDuringScan_ReturnsNotFoundBeforeRequestDeadline()
    {
        var hay = BgraFrames.Noise(1280, 720, seed: 3);
        var tmpl = BgraFrames.Checker(48, 48, cell: 3);
        var sw = Stopwatch.StartNew();
        var result = ZnccMatcher.Match(
            hay, 1280, 720, 1280 * 4,
            tmpl, 48, 48, 48 * 4,
            Limits.V1.TemplateScaleMin,
            Limits.V1.TemplateScaleMax,
            CancellationToken.None,
            searchTimeoutMs: 15);
        sw.Stop();

        Assert.Equal(TemplateMatchStatus.NotFound, result.Status);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), sw.Elapsed.ToString());
        Assert.True(sw.Elapsed < TimeSpan.FromMilliseconds(Limits.V1.RequestDeadlineMs / 4), sw.Elapsed.ToString());
    }

    private static void Paste(byte[] dest, int destW, byte[] src, int srcW, int srcH, int x, int y)
    {
        for (var row = 0; row < srcH; row++)
        {
            var destOff = ((y + row) * destW + x) * 4;
            var srcOff = row * srcW * 4;
            Buffer.BlockCopy(src, srcOff, dest, destOff, srcW * 4);
        }
    }
}

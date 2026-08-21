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

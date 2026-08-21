using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Identity;
using ComputerUse.Mcp.Tests.Support;

namespace ComputerUse.Mcp.Tests;

public sealed class ControlMemoryContractTests
{
    [Fact]
    public void NewErrorCodes_UseStableSnakeCase()
    {
        Assert.Equal("frame_not_visualized", ErrorCodes.FrameNotVisualized);
        Assert.Equal("screen_unknown", ErrorCodes.ScreenUnknown);
        Assert.Equal("screen_ambiguous", ErrorCodes.ScreenAmbiguous);
        Assert.Equal("screen_mismatch", ErrorCodes.ScreenMismatch);
        Assert.Equal("template_not_found", ErrorCodes.TemplateNotFound);
        Assert.Equal("template_ambiguous", ErrorCodes.TemplateAmbiguous);
        Assert.Equal("template_scale_mismatch", ErrorCodes.TemplateScaleMismatch);
        Assert.Equal("unknown_control", ErrorCodes.UnknownControl);
        Assert.Equal("low_entropy_crop", ErrorCodes.LowEntropyCrop);
    }

    [Fact]
    public void MemoryQuotaLimits_MatchPhase1Defaults()
    {
        var limits = Limits.V1;
        Assert.Equal(32, limits.MaxScreensPerAppKey);
        Assert.Equal(64, limits.MaxControlsPerScreen);
        Assert.Equal(256, limits.MaxTemplateLongEdge);
        Assert.Equal(256 * 1024 * 1024, limits.MaxMemoryLibraryBytes);
        Assert.Equal(30, limits.MemorySoftTtlDays);
        Assert.Equal(24, limits.MinCropEdgePx);
        Assert.Equal(0.85, limits.TemplateScaleMin);
        Assert.Equal(1.15, limits.TemplateScaleMax);
    }

    [Fact]
    public void FrameRecord_DefaultsToNotVisualizedAndNoPixels()
    {
        var frame = new FrameRecord
        {
            FrameId = "fr1.test",
            TargetToken = "tok",
            Hwnd = 1,
            Pid = 1,
            CreateTimeUtc = 1,
            ClassName = "Notepad",
            Width = 100,
            Height = 50,
            SourceWidth = 100,
            SourceHeight = 50,
            Scale = 1,
            CaptureMethod = "wgc",
            WindowRect = new ScreenRect(0, 0, 100, 50),
            ExtendedFrameBounds = new ScreenRect(0, 0, 100, 50),
            CaptureOriginScreen = new ScreenPoint(0, 0),
            Dpi = Dpi.Default,
            MonitorDeviceName = @"\\.\DISPLAY1",
            CapturedAt = DateTimeOffset.UtcNow,
            Rounding = CoordinateMapper.Rounding
        };

        Assert.False(frame.ImageReturnedToClient);
        Assert.Null(frame.Bgra);
        Assert.Equal(0, frame.BgraStride);
    }

    [Fact]
    public void BgraFrames_SolidIsUniform_NoiseAndCheckerDiffer()
    {
        var solid = BgraFrames.Solid(8, 8, 10, 20, 30);
        Assert.Equal(8 * 8 * 4, solid.Length);
        Assert.Equal(10, solid[0]);
        Assert.Equal(20, solid[1]);
        Assert.Equal(30, solid[2]);
        Assert.Equal(255, solid[3]);

        var noise = BgraFrames.Noise(8, 8);
        var checker = BgraFrames.Checker(8, 8);
        Assert.NotEqual(solid, noise);
        Assert.NotEqual(noise, checker);
        Assert.Equal(255, checker[3]);
    }

    [Fact]
    public void PublicLimitsDto_DoesNotGainMemoryQuotaFields()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(Limits.V1.ToPublicDto());
        Assert.DoesNotContain("maxScreensPerAppKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("minCropEdgePx", json, StringComparison.OrdinalIgnoreCase);
    }
}

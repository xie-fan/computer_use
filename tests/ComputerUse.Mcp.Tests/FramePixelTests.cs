using ComputerUse.Mcp.Capture;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Identity;
using ComputerUse.Mcp.Tests.Support;

namespace ComputerUse.Mcp.Tests;

public sealed class FramePixelTests
{
    [Fact]
    public void PointerOperate_OnUnvisualizedFrame_IsFrameNotVisualized()
    {
        var frame = CreateFrame(visualized: false);
        var ex = Assert.Throws<ComputerUseException>(() =>
            FrameVisualization.EnsurePointerMayUse(frame, hasPointerActions: true));
        Assert.Equal(ErrorCodes.FrameNotVisualized, ex.Code);
    }

    [Fact]
    public void NonPointerOperate_OnUnvisualizedFrame_DoesNotRequireVisualized()
    {
        var frame = CreateFrame(visualized: false);
        FrameVisualization.EnsurePointerMayUse(frame, hasPointerActions: false);
    }

    [Fact]
    public void PointerOperate_OnVisualizedFrame_DoesNotThrow()
    {
        var frame = CreateFrame(visualized: true);
        FrameVisualization.EnsurePointerMayUse(frame, hasPointerActions: true);
    }

    [Fact]
    public void FitLongEdge_ReturnsFittedBgraMatchingDimensions()
    {
        var src = BgraFrames.Noise(10, 4);
        var fitted = PngCodec.FitLongEdge(src, 10, 4, 40, maxLongEdge: 5, maxPngBytes: 1_000_000);
        Assert.Equal(5, fitted.Width);
        Assert.Equal(2, fitted.Height);
        Assert.NotNull(fitted.Bgra);
        Assert.Equal(fitted.Width * fitted.Height * 4, fitted.Bgra.Length);
    }

    [Fact]
    public void FrameCache_RetainsBgraUntilEvicted()
    {
        var limits = Limits.V1 with { MaxCachedFrames = 1 };
        var cache = new FrameCache(limits);
        var pixels = BgraFrames.Checker(8, 8);
        var frame = CreateFrame(visualized: true, bgra: pixels, frameId: "fr1.keep");
        cache.Add(frame);
        var loaded = cache.Require("fr1.keep");
        Assert.Equal(pixels, loaded.Bgra);

        cache.Add(CreateFrame(visualized: true, frameId: "fr1.other"));
        var stale = Assert.Throws<ComputerUseException>(() => cache.Require("fr1.keep"));
        Assert.Equal(ErrorCodes.StaleCapture, stale.Code);
    }

    private static FrameRecord CreateFrame(bool visualized, byte[]? bgra = null, string frameId = "fr1.test") => new()
    {
        FrameId = frameId,
        TargetToken = "tok",
        Hwnd = 1,
        Pid = 1,
        CreateTimeUtc = 1,
        ClassName = "Notepad",
        Width = 8,
        Height = 8,
        SourceWidth = 8,
        SourceHeight = 8,
        Scale = 1,
        CaptureMethod = "wgc",
        WindowRect = new ScreenRect(0, 0, 8, 8),
        ExtendedFrameBounds = new ScreenRect(0, 0, 8, 8),
        CaptureOriginScreen = new ScreenPoint(0, 0),
        Dpi = Dpi.Default,
        MonitorDeviceName = @"\\.\DISPLAY1",
        CapturedAt = DateTimeOffset.UtcNow,
        Rounding = CoordinateMapper.Rounding,
        Bgra = bgra,
        BgraStride = bgra is null ? 0 : 32,
        ImageReturnedToClient = visualized
    };
}

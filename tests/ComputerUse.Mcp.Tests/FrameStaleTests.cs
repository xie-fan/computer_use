using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Identity;

namespace ComputerUse.Mcp.Tests;

public sealed class FrameStaleTests
{
    [Fact]
    public void PointerAction_OnResize_IsStaleCapture()
    {
        var limits = Limits.V1;
        var cache = new FrameCache(limits);
        var frame = CreateFrame(new ScreenRect(0, 0, 200, 100));
        cache.Add(frame);

        var live = new WindowGeometry
        {
            WindowRect = new ScreenRect(0, 0, 400, 200),
            ExtendedFrameBounds = new ScreenRect(0, 0, 400, 200),
            Dpi = Dpi.Default
        };

        var loaded = cache.Require(frame.FrameId);
        var ex = Assert.Throws<ComputerUseException>(() => cache.EnsureGeometryIfPointer(loaded, live, hasPointerActions: true));
        Assert.Equal(ErrorCodes.StaleCapture, ex.Code);
    }

    [Fact]
    public void NonPointerAction_OnResize_DoesNotStaleCapture()
    {
        var cache = new FrameCache(Limits.V1);
        var frame = CreateFrame(new ScreenRect(10, 10, 200, 100));
        cache.Add(frame);
        var live = new WindowGeometry
        {
            WindowRect = new ScreenRect(10, 10, 400, 200),
            ExtendedFrameBounds = new ScreenRect(10, 10, 400, 200),
            Dpi = Dpi.Default
        };
        cache.EnsureGeometryIfPointer(cache.Require(frame.FrameId), live, hasPointerActions: false);
    }

    [Fact]
    public void DpiChange_OnPointerAction_IsStaleCapture()
    {
        var cache = new FrameCache(Limits.V1);
        var frame = CreateFrame(new ScreenRect(0, 0, 200, 100));
        cache.Add(frame);
        var live = new WindowGeometry
        {
            WindowRect = frame.WindowRect,
            ExtendedFrameBounds = frame.ExtendedFrameBounds,
            Dpi = new Dpi(144, 144)
        };
        var ex = Assert.Throws<ComputerUseException>(() => cache.EnsureGeometryIfPointer(cache.Require(frame.FrameId), live, true));
        Assert.Equal(ErrorCodes.StaleCapture, ex.Code);
    }

    [Fact]
    public void UnknownFrameId_IsStaleCapture()
    {
        var cache = new FrameCache(Limits.V1);
        var ex = Assert.Throws<ComputerUseException>(() => cache.Require("missing"));
        Assert.Equal(ErrorCodes.StaleCapture, ex.Code);
    }

    [Fact]
    public void FrameTargetMismatch_IsStaleTarget()
    {
        var cache = new FrameCache(Limits.V1);
        var frame = CreateFrame(new ScreenRect(0, 0, 100, 100));
        cache.Add(frame);
        var other = new TargetTokenPayload
        {
            Hwnd = 99,
            Pid = 2,
            CreateTimeUtc = 1,
            ClassName = "X",
            IssuedUnixMs = 1
        };
        var ex = Assert.Throws<ComputerUseException>(() => cache.EnsureMatchesToken(frame, other));
        Assert.Equal(ErrorCodes.StaleTarget, ex.Code);
    }

    private static FrameRecord CreateFrame(ScreenRect rect) => new()
    {
        FrameId = "fr1.test",
        TargetToken = "tok",
        Hwnd = 1,
        Pid = 1,
        CreateTimeUtc = 1,
        ClassName = "Notepad",
        Width = 100,
        Height = 50,
        SourceWidth = rect.Width,
        SourceHeight = rect.Height,
        Scale = 1,
        CaptureMethod = "wgc",
        WindowRect = rect,
        ExtendedFrameBounds = rect,
        CaptureOriginScreen = new ScreenPoint(rect.Left, rect.Top),
        Dpi = Dpi.Default,
        MonitorDeviceName = @"\\.\DISPLAY1",
        CapturedAt = DateTimeOffset.UtcNow,
        Rounding = "floor"
    };
}

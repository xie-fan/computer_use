using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Identity;
using ComputerUse.Mcp.Tests.Support;

namespace ComputerUse.Mcp.Tests.Support;

internal static class TestFrames
{
    public static FrameRecord Create(
        int width,
        int height,
        byte[]? bgra = null,
        bool visualized = true,
        string frameId = "fr1.test",
        nint hwnd = 1,
        uint pid = 1,
        double scale = 1,
        int? sourceWidth = null,
        int? sourceHeight = null,
        ScreenPoint? captureOrigin = null)
    {
        var srcW = sourceWidth ?? width;
        var srcH = sourceHeight ?? height;
        var origin = captureOrigin ?? new ScreenPoint(0, 0);
        return new()
        {
            FrameId = frameId,
            TargetToken = "tok",
            Hwnd = hwnd,
            Pid = pid,
            CreateTimeUtc = 1,
            ClassName = "Notepad",
            Width = width,
            Height = height,
            SourceWidth = srcW,
            SourceHeight = srcH,
            Scale = scale,
            CaptureMethod = "wgc",
            WindowRect = new ScreenRect(origin.X, origin.Y, origin.X + srcW, origin.Y + srcH),
            ExtendedFrameBounds = new ScreenRect(origin.X, origin.Y, origin.X + srcW, origin.Y + srcH),
            CaptureOriginScreen = origin,
            Dpi = Dpi.Default,
            MonitorDeviceName = @"\\.\DISPLAY1",
            CapturedAt = DateTimeOffset.UtcNow,
            Rounding = CoordinateMapper.Rounding,
            Bgra = bgra,
            BgraStride = bgra is null ? 0 : width * 4,
            ImageReturnedToClient = visualized
        };
    }
}

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
        uint pid = 1)
        => new()
        {
            FrameId = frameId,
            TargetToken = "tok",
            Hwnd = hwnd,
            Pid = pid,
            CreateTimeUtc = 1,
            ClassName = "Notepad",
            Width = width,
            Height = height,
            SourceWidth = width,
            SourceHeight = height,
            Scale = 1,
            CaptureMethod = "wgc",
            WindowRect = new ScreenRect(0, 0, width, height),
            ExtendedFrameBounds = new ScreenRect(0, 0, width, height),
            CaptureOriginScreen = new ScreenPoint(0, 0),
            Dpi = Dpi.Default,
            MonitorDeviceName = @"\\.\DISPLAY1",
            CapturedAt = DateTimeOffset.UtcNow,
            Rounding = CoordinateMapper.Rounding,
            Bgra = bgra,
            BgraStride = bgra is null ? 0 : width * 4,
            ImageReturnedToClient = visualized
        };
}

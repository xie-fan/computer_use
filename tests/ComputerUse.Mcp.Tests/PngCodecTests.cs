using ComputerUse.Mcp.Capture;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Identity;

namespace ComputerUse.Mcp.Tests;

public sealed class PngCodecTests
{
    [Fact]
    public void FitLongEdge_WritesScaleConsistentWithFloorMapping()
    {
        var bgra = Solid(10, 4, 0xFF, 0x00, 0x00, 0xFF);
        var fitted = PngCodec.FitLongEdge(bgra, 10, 4, 40, maxLongEdge: 5, maxPngBytes: 1_000_000);
        Assert.Equal(5, fitted.Width);
        Assert.Equal(2, fitted.Height);
        Assert.Equal(0.5, fitted.Scale);

        var frame = Frame(fitted.Width, fitted.Height, 10, 4, fitted.Scale);
        var mapped = CoordinateMapper.MapImageToScreen(frame, 4, 1);
        Assert.Equal(8, mapped.X);
        Assert.Equal(2, mapped.Y);
    }

    [Fact]
    public void FitLongEdge_OverMaxPngBytes_ShrinksInsteadOfFailingImmediately()
    {
        var bgra = Noise(48, 48);
        var fitted = PngCodec.FitLongEdge(bgra, 48, 48, 48 * 4, maxLongEdge: 48, maxPngBytes: 400);
        Assert.True(fitted.Png.Length <= 400);
        Assert.True(fitted.Width < 48 || fitted.Height < 48);
        var frame = Frame(fitted.Width, fitted.Height, 48, 48, fitted.Scale);
        var mapped = CoordinateMapper.MapImageToScreen(frame, fitted.Width - 1, fitted.Height - 1);
        Assert.InRange(mapped.X, 0, 47);
        Assert.InRange(mapped.Y, 0, 47);
    }

    [Fact]
    public void FitLongEdge_TinyBudget_StillFailsAfterShrinks()
    {
        var bgra = Noise(16, 16);
        var ex = Assert.Throws<ComputerUseException>(() =>
            PngCodec.FitLongEdge(bgra, 16, 16, 64, maxLongEdge: 16, maxPngBytes: 1));
        Assert.Equal(ErrorCodes.PayloadTooLarge, ex.Code);
    }

    private static FrameRecord Frame(int width, int height, int sourceW, int sourceH, double scale) => new()
    {
        FrameId = "fr1.test",
        TargetToken = "tok",
        Hwnd = 1,
        Pid = 1,
        CreateTimeUtc = 1,
        ClassName = "Notepad",
        Width = width,
        Height = height,
        SourceWidth = sourceW,
        SourceHeight = sourceH,
        Scale = scale,
        CaptureMethod = "wgc",
        WindowRect = new ScreenRect(0, 0, sourceW, sourceH),
        ExtendedFrameBounds = new ScreenRect(0, 0, sourceW, sourceH),
        CaptureOriginScreen = new ScreenPoint(0, 0),
        Dpi = Dpi.Default,
        MonitorDeviceName = @"\\.\DISPLAY1",
        CapturedAt = DateTimeOffset.UtcNow,
        Rounding = CoordinateMapper.Rounding
    };

    private static byte[] Solid(int width, int height, byte b, byte g, byte r, byte a)
    {
        var data = new byte[width * height * 4];
        for (var i = 0; i < data.Length; i += 4)
        {
            data[i] = b;
            data[i + 1] = g;
            data[i + 2] = r;
            data[i + 3] = a;
        }
        return data;
    }

    private static byte[] Noise(int width, int height)
    {
        var data = new byte[width * height * 4];
        for (var i = 0; i < data.Length; i++)
            data[i] = (byte)(i * 37 + 11);
        return data;
    }
}

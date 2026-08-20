namespace ComputerUse.Mcp.Domain;

internal readonly record struct ScreenRect(int Left, int Top, int Width, int Height)
{
    public int Right => checked(Left + Width);
    public int Bottom => checked(Top + Height);
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public object ToDto() => new { left = Left, top = Top, width = Width, height = Height };

    public bool ApproximatelyEquals(ScreenRect other, int epsilon) =>
        Math.Abs(Left - other.Left) <= epsilon
        && Math.Abs(Top - other.Top) <= epsilon
        && Math.Abs(Width - other.Width) <= epsilon
        && Math.Abs(Height - other.Height) <= epsilon;
}

internal readonly record struct ScreenPoint(int X, int Y);

internal readonly record struct Dpi(uint X, uint Y)
{
    public static Dpi Default { get; } = new(96, 96);
    public bool EqualsExact(Dpi other) => X == other.X && Y == other.Y;
}

internal sealed class MonitorInfo
{
    public required string DeviceName { get; init; }
    public required bool Primary { get; init; }
    public required ScreenRect Bounds { get; init; }
    public required ScreenRect WorkArea { get; init; }
    public required Dpi Dpi { get; init; }
    public required int Index { get; init; }
    public nint Handle { get; init; }

    public object ToDto() => new
    {
        deviceName = DeviceName,
        primary = Primary,
        bounds = Bounds.ToDto(),
        workArea = WorkArea.ToDto(),
        dpi = new { x = Dpi.X, y = Dpi.Y },
        index = Index
    };
}

internal sealed class WindowGeometry
{
    public required ScreenRect WindowRect { get; init; }
    public required ScreenRect ExtendedFrameBounds { get; init; }
    public required Dpi Dpi { get; init; }
}

internal sealed class CapturedBitmap
{
    public required byte[] Bgra { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int Stride { get; init; }
    public required string Method { get; init; }
}

internal sealed class FrameRecord
{
    public required string FrameId { get; init; }
    public required string TargetToken { get; init; }
    public required nint Hwnd { get; init; }
    public required uint Pid { get; init; }
    public required long CreateTimeUtc { get; init; }
    public required string ClassName { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int SourceWidth { get; init; }
    public required int SourceHeight { get; init; }
    public required double Scale { get; init; }
    public required string CaptureMethod { get; init; }
    public required ScreenRect WindowRect { get; init; }
    public required ScreenRect ExtendedFrameBounds { get; init; }
    public required ScreenPoint CaptureOriginScreen { get; init; }
    public required Dpi Dpi { get; init; }
    public required string MonitorDeviceName { get; init; }
    public required DateTimeOffset CapturedAt { get; init; }
    public required string Rounding { get; init; }

    public object ToTransformDto() => new
    {
        rounding = Rounding,
        scale = Scale,
        captureOriginScreen = new { x = CaptureOriginScreen.X, y = CaptureOriginScreen.Y },
        captureSize = new { width = SourceWidth, height = SourceHeight },
        windowRect = WindowRect.ToDto(),
        extendedFrameBounds = ExtendedFrameBounds.ToDto()
    };

    public bool GeometryChanged(WindowGeometry live, int epsilon) =>
        !WindowRect.ApproximatelyEquals(live.WindowRect, epsilon)
        || !ExtendedFrameBounds.ApproximatelyEquals(live.ExtendedFrameBounds, epsilon)
        || !Dpi.EqualsExact(live.Dpi);
}

internal sealed class TargetTokenPayload
{
    public required nint Hwnd { get; init; }
    public required uint Pid { get; init; }
    public required long CreateTimeUtc { get; init; }
    public required string ClassName { get; init; }
    public required long IssuedUnixMs { get; init; }
}

internal enum IntegrityLevel
{
    Unknown = -1,
    Untrusted = 0x0000,
    Low = 0x1000,
    Medium = 0x2000,
    MediumPlus = 0x2100,
    High = 0x3000,
    System = 0x4000,
    Protected = 0x5000
}

internal enum MouseButtonKind
{
    Left,
    Right,
    Middle
}

internal enum SessionDenial
{
    None,
    SecureDesktop,
    NotInteractive
}

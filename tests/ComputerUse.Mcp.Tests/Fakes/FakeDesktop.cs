using ComputerUse.Mcp.Abstractions;
using ComputerUse.Mcp.Domain;

namespace ComputerUse.Mcp.Tests.Fakes;

internal sealed class FakeSession : ISessionGuard
{
    public SessionDenial Denial { get; set; } = SessionDenial.None;
    public SessionDenial Evaluate() => Denial;
}

internal sealed class FakeActivator : IWindowActivator
{
    public nint Foreground { get; set; }
    public bool ActivateResult { get; set; } = true;

    public nint GetForegroundWindow() => Foreground;
    public RestoreAttempt RestoreIfMinimized(nint hwnd, TimeSpan timeout) => new(false, false, Foreground, Foreground);
    public bool TryActivate(nint hwnd)
    {
        if (!ActivateResult)
            return false;
        Foreground = hwnd;
        return true;
    }
}

internal sealed class FakeCapture : ICapturePipeline
{
    public required byte[] Pixels { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public Task<CapturedBitmap> CaptureAsync(nint hwnd, int timeoutMs, CancellationToken cancellationToken)
    {
        var stride = Width * 4;
        var captured = CapturedBitmap.Rent(Width, Height, stride, "fake");
        Buffer.BlockCopy(Pixels, 0, captured.Bgra, 0, Math.Min(Pixels.Length, captured.ByteLength));
        return Task.FromResult(captured);
    }
}

internal sealed class FakeHitTester : IHitTester
{
    public nint Hit { get; set; }
    public nint WindowFromPhysicalPoint(int x, int y) => Hit;
}

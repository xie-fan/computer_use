using ComputerUse.Mcp.Abstractions;
using ComputerUse.Mcp.Capture;
using ComputerUse.Mcp.Coordination;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Identity;
using Microsoft.Extensions.Logging;

namespace ComputerUse.Mcp.Services;

internal sealed class ScreenshotService
{
    private readonly DesktopOperationCoordinator _coordinator;
    private readonly TargetTokenService _tokens;
    private readonly FrameCache _frames;
    private readonly IWindowQuery _windows;
    private readonly IMonitorQuery _monitors;
    private readonly IProcessQuery _processes;
    private readonly IVirtualDesktopMembership _desktops;
    private readonly ISessionGuard _session;
    private readonly IWindowActivator _activator;
    private readonly ICapturePipeline _capture;
    private readonly WindowListService _list;
    private readonly Limits _limits;
    private readonly ILogger<ScreenshotService> _logger;

    public ScreenshotService(
        DesktopOperationCoordinator coordinator,
        TargetTokenService tokens,
        FrameCache frames,
        IWindowQuery windows,
        IMonitorQuery monitors,
        IProcessQuery processes,
        IVirtualDesktopMembership desktops,
        ISessionGuard session,
        IWindowActivator activator,
        ICapturePipeline capture,
        WindowListService list,
        Limits limits,
        ILogger<ScreenshotService> logger)
    {
        _coordinator = coordinator;
        _tokens = tokens;
        _frames = frames;
        _windows = windows;
        _monitors = monitors;
        _processes = processes;
        _desktops = desktops;
        _session = session;
        _activator = activator;
        _capture = capture;
        _list = list;
        _limits = limits;
        _logger = logger;
    }

    public Task<(object Json, byte[] Png)> CaptureAsync(string targetToken, CancellationToken cancellationToken) =>
        _coordinator.RunAsync(ct => CaptureLockedAsync(targetToken, ct), cancellationToken);

    private async Task<(object Json, byte[] Png)> CaptureLockedAsync(string targetToken, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var side = new SideEffects();
        try
        {
            var token = _tokens.RequireValid(targetToken, _windows, _processes);
            AccessGuards.EnsureInteractive(_session);
            AccessGuards.EnsureCurrentDesktop(_desktops, token.Hwnd);
            AccessGuards.EnsureIntegrity(_processes, token.Pid);

            var fgBefore = _activator.GetForegroundWindow();
            var restore = _activator.RestoreIfMinimized(token.Hwnd, TimeSpan.FromMilliseconds(_limits.RestoreTimeoutMs));
            side.WindowRestored = restore.PostedRestore;
            var fgAfter = _activator.GetForegroundWindow();
            side.ForegroundChanged = fgAfter != fgBefore;

            var live = ReadGeometry(token.Hwnd);
            var captured = await _capture.CaptureAsync(token.Hwnd, _limits.CaptureTimeoutMs, cancellationToken).ConfigureAwait(false);
            var fitted = PngCodec.FitLongEdge(captured.Bgra, captured.Width, captured.Height, captured.Stride, _limits.MaxReturnedLongEdge, _limits.MaxPngBytes);

            var frameId = "fr1." + Guid.NewGuid().ToString("N");
            var capturedAt = DateTimeOffset.UtcNow;
            var monitors = _monitors.EnumerateMonitors();
            var monitor = _monitors.FromWindow(token.Hwnd, monitors);
            var origin = new ScreenPoint(live.WindowRect.Left, live.WindowRect.Top);

            var frame = new FrameRecord
            {
                FrameId = frameId,
                TargetToken = targetToken,
                Hwnd = token.Hwnd,
                Pid = token.Pid,
                CreateTimeUtc = token.CreateTimeUtc,
                ClassName = token.ClassName,
                Width = fitted.Width,
                Height = fitted.Height,
                SourceWidth = captured.Width,
                SourceHeight = captured.Height,
                Scale = fitted.Scale,
                CaptureMethod = captured.Method,
                WindowRect = live.WindowRect,
                ExtendedFrameBounds = live.ExtendedFrameBounds,
                CaptureOriginScreen = origin,
                Dpi = live.Dpi,
                MonitorDeviceName = monitor?.DeviceName ?? "",
                CapturedAt = capturedAt,
                Rounding = CoordinateMapper.Rounding
            };
            _frames.Add(frame);

            var json = new
            {
                frameId,
                targetToken,
                width = fitted.Width,
                height = fitted.Height,
                sourceWidth = captured.Width,
                sourceHeight = captured.Height,
                scale = fitted.Scale,
                captureMethod = captured.Method,
                transform = frame.ToTransformDto(),
                dpi = new { x = live.Dpi.X, y = live.Dpi.Y },
                bounds = live.WindowRect.ToDto(),
                monitor = new { deviceName = monitor?.DeviceName },
                capturedAt,
                sideEffects = side,
                contractVersion = Contract.Version,
                serverVersion = Contract.ServerVersion,
                capabilities = _list.Capabilities(),
                limits = _limits.ToPublicDto()
            };

            _logger.LogInformation("tool={Tool} code={Code} elapsedMs={Elapsed}", "screenshot_window", "ok", (int)(DateTimeOffset.UtcNow - started).TotalMilliseconds);
            return (json, fitted.Png);
        }
        catch (ComputerUseException ex)
        {
            _logger.LogInformation("tool={Tool} code={Code} elapsedMs={Elapsed}", "screenshot_window", ex.Code, (int)(DateTimeOffset.UtcNow - started).TotalMilliseconds);
            throw ex.WithDetails(new { sideEffects = side });
        }
    }

    private WindowGeometry ReadGeometry(nint hwnd) => new()
    {
        WindowRect = _windows.GetWindowRect(hwnd),
        ExtendedFrameBounds = _windows.GetExtendedFrameBounds(hwnd),
        Dpi = _windows.GetDpi(hwnd)
    };
}

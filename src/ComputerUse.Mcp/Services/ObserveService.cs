using ComputerUse.Mcp.Abstractions;
using ComputerUse.Mcp.Capture;
using ComputerUse.Mcp.Coordination;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Identity;
using ComputerUse.Mcp.Memory;
using ComputerUse.Mcp.Vision;

namespace ComputerUse.Mcp.Services;

internal sealed class ObserveResult
{
    public string? ScreenId { get; init; }
    public string? ScreenKey { get; init; }
    public IReadOnlyList<RememberedControl> Controls { get; init; } = [];
    public required string FrameId { get; init; }
    public bool Visualized { get; init; }
    public bool HostWindow { get; init; }
    public string? MemoryHint { get; init; }
}

internal sealed class ObserveService
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
    private readonly IHostProcessResolver _host;
    private readonly MemoryCatalog _catalog;
    private readonly AppIdentityFactory _identities;
    private readonly Limits _limits;

    public ObserveService(
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
        IHostProcessResolver host,
        MemoryCatalog catalog,
        Limits limits,
        AppIdentityFactory identities)
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
        _host = host;
        _catalog = catalog;
        _limits = limits;
        _identities = identities;
    }

    public Task<ObserveResult> ObserveAsync(string targetToken, CancellationToken cancellationToken) =>
        _coordinator.RunAsync(ct => ObserveLockedAsync(targetToken, ct), cancellationToken);

    private async Task<ObserveResult> ObserveLockedAsync(string targetToken, CancellationToken cancellationToken)
    {
        var side = new SideEffects();
        CapturedBitmap? captured = null;
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
            captured = await _capture.CaptureAsync(token.Hwnd, _limits.CaptureTimeoutMs, cancellationToken).ConfigureAwait(false);
            var fitted = PngCodec.FitLongEdge(
                captured.Bgra,
                captured.Width,
                captured.Height,
                captured.Stride,
                _limits.MaxReturnedLongEdge,
                _limits.MaxPngBytes);

            var frameId = "fr1." + Guid.NewGuid().ToString("N");
            var capturedAt = DateTimeOffset.UtcNow;
            var monitors = _monitors.EnumerateMonitors();
            var monitor = _monitors.FromWindow(token.Hwnd, monitors);
            var origin = new ScreenPoint(live.WindowRect.Left, live.WindowRect.Top);

            _frames.Add(new FrameRecord
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
                Rounding = CoordinateMapper.Rounding,
                Bgra = fitted.Bgra,
                BgraStride = fitted.Width * 4,
                ImageReturnedToClient = false
            });

            var hostWindow = _host.IsHostProcess(token.Pid, token.CreateTimeUtc);
            if (hostWindow)
            {
                return new ObserveResult
                {
                    FrameId = frameId,
                    Visualized = false,
                    HostWindow = true
                };
            }

            return RecognizeScreen(token, fitted.Bgra, fitted.Width, fitted.Height, frameId, cancellationToken);
        }
        catch (ComputerUseException ex)
        {
            throw ex.Details is null
                ? ex.WithDetails(new { sideEffects = side })
                : ex;
        }
        finally
        {
            captured?.Return();
        }
    }

    private ObserveResult RecognizeScreen(
        TargetTokenPayload token,
        byte[] frameBgra,
        int width,
        int height,
        string frameId,
        CancellationToken cancellationToken)
    {
        var appKey = _identities.Resolve(token.Pid, token.CreateTimeUtc, token.ClassName);

        var assets = _catalog.LoadAppScreens(appKey.Value);
        var identified = ScreenIdentifier.Identify(
            frameBgra,
            width,
            height,
            width * 4,
            ScreenIdentifier.FromCatalog(assets),
            loadNominatedControls: screenId => ScreenIdentifier.ControlsFrom(
                _catalog.LoadScreenControls(appKey.Value, screenId)),
            cancellationToken: cancellationToken);

        if (identified.Status == ScreenIdentifyStatus.Ambiguous)
        {
            throw new ComputerUseException(
                ErrorCodes.ScreenAmbiguous,
                "Multiple remembered screens match the current frame equally well.",
                new { candidates = identified.CandidateIds });
        }

        if (identified.Status == ScreenIdentifyStatus.Identified
            && identified.ScreenId is not null
            && TryFindScreen(assets, identified.ScreenId, out var match))
        {
            return new ObserveResult
            {
                ScreenId = match.ScreenId,
                ScreenKey = match.ScreenKey,
                Controls = ToRememberedControls(match.Controls),
                FrameId = frameId,
                Visualized = false,
                HostWindow = false
            };
        }

        return new ObserveResult
        {
            FrameId = frameId,
            Visualized = false,
            HostWindow = false,
            MemoryHint = assets.Count == 0
                ? "This AppKey has no remembered screens yet."
                : null
        };
    }

    private static bool TryFindScreen(
        IReadOnlyList<CatalogScreenAssets> assets,
        string screenId,
        out CatalogScreenAssets match)
    {
        for (var i = 0; i < assets.Count; i++)
        {
            if (string.Equals(assets[i].ScreenId, screenId, StringComparison.Ordinal))
            {
                match = assets[i];
                return true;
            }
        }

        match = null!;
        return false;
    }

    private static IReadOnlyList<RememberedControl> ToRememberedControls(IReadOnlyList<CatalogControl> controls)
    {
        var remembered = new RememberedControl[controls.Count];
        for (var i = 0; i < controls.Count; i++)
        {
            var control = controls[i];
            remembered[i] = new RememberedControl(control.ControlId, control.Name);
        }

        return remembered;
    }

    private WindowGeometry ReadGeometry(nint hwnd) => new()
    {
        WindowRect = _windows.GetWindowRect(hwnd),
        ExtendedFrameBounds = _windows.GetExtendedFrameBounds(hwnd),
        Dpi = _windows.GetDpi(hwnd)
    };
}

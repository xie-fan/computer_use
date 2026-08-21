using ComputerUse.Mcp.Abstractions;
using ComputerUse.Mcp.Capture;
using ComputerUse.Mcp.Coordination;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Identity;
using ComputerUse.Mcp.Input;
using ComputerUse.Mcp.Memory;
using ComputerUse.Mcp.Vision;

namespace ComputerUse.Mcp.Services;

internal sealed class ClickControlService
{
    private const double RoiExpandFactor = 0.20;

    private readonly DesktopOperationCoordinator _coordinator;
    private readonly OperationIdCache _operationIds;
    private readonly TargetTokenService _tokens;
    private readonly FrameCache _frames;
    private readonly IWindowQuery _windows;
    private readonly IMonitorQuery _monitors;
    private readonly IProcessQuery _processes;
    private readonly IVirtualDesktopMembership _desktops;
    private readonly ISessionGuard _session;
    private readonly IWindowActivator _activator;
    private readonly IHitTester _hitTester;
    private readonly IInputInjector _input;
    private readonly ICapturePipeline _capture;
    private readonly IHostProcessResolver _host;
    private readonly MemoryCatalog _catalog;
    private readonly Limits _limits;
    private readonly AppIdentityFactory _identities;

    public ClickControlService(
        DesktopOperationCoordinator coordinator,
        OperationIdCache operationIds,
        TargetTokenService tokens,
        FrameCache frames,
        IWindowQuery windows,
        IMonitorQuery monitors,
        IProcessQuery processes,
        IVirtualDesktopMembership desktops,
        ISessionGuard session,
        IWindowActivator activator,
        IHitTester hitTester,
        IInputInjector input,
        ICapturePipeline capture,
        IHostProcessResolver host,
        MemoryCatalog catalog,
        Limits limits,
        AppIdentityFactory identities)
    {
        _coordinator = coordinator;
        _operationIds = operationIds;
        _tokens = tokens;
        _frames = frames;
        _windows = windows;
        _monitors = monitors;
        _processes = processes;
        _desktops = desktops;
        _session = session;
        _activator = activator;
        _hitTester = hitTester;
        _input = input;
        _capture = capture;
        _host = host;
        _catalog = catalog;
        _limits = limits;
        _identities = identities;
    }

    public Task<object> ClickAsync(string targetToken, string controlId, string? operationId, CancellationToken cancellationToken) =>
        _coordinator.RunAsync(ct => ClickLockedAsync(targetToken, controlId, operationId, ct), cancellationToken);

    private async Task<object> ClickLockedAsync(
        string targetToken,
        string controlId,
        string? operationId,
        CancellationToken cancellationToken)
    {
        if (operationId is not null)
        {
            var existing = _operationIds.TryBegin(operationId);
            if (existing is not null)
            {
                if (!existing.OutcomeKnown)
                    throw new ComputerUseException(ErrorCodes.DuplicateInFlight, "This operationId is already in flight.");
                if (existing.IsError)
                    throw new ComputerUseException(existing.Code ?? ErrorCodes.ActionFailed, existing.Message ?? "The previous operation failed.", existing.Result);
                if (existing.Result is object cached)
                    return cached;
            }
        }

        var tracker = new InjectionTracker(_input);
        var mayHaveExecuted = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var body = await ClickCoreAsync(targetToken, controlId, tracker, cancellationToken).ConfigureAwait(false);
            mayHaveExecuted = true;
            if (operationId is not null)
                _operationIds.Complete(operationId, body, true, false);
            return body;
        }
        catch (ComputerUseException ex)
        {
            if (operationId is not null)
                _operationIds.Complete(operationId, ex.Details ?? new { }, true, true, ex.Code, ex.Message);
            throw;
        }
        catch (OperationCanceledException)
        {
            var code = cancellationToken.IsCancellationRequested ? ErrorCodes.Cancelled : ErrorCodes.Timeout;
            var details = new { mayHaveExecuted, code };
            if (operationId is not null)
                _operationIds.Complete(operationId, details, false, true, code, "The click_control request did not finish.");
            throw new ComputerUseException(code, "The click_control request did not finish.", details);
        }
        finally
        {
            tracker.ReleaseAll();
        }
    }

    private async Task<object> ClickCoreAsync(
        string targetToken,
        string controlId,
        InjectionTracker tracker,
        CancellationToken cancellationToken)
    {
        var token = _tokens.RequireValid(targetToken, _windows, _processes);
        AccessGuards.EnsureInteractive(_session);
        AccessGuards.EnsureCurrentDesktop(_desktops, token.Hwnd);
        AccessGuards.EnsureIntegrity(_processes, token.Pid);
        _input.RefreshMetrics();

        if (_host.IsHostProcess(token.Pid))
            throw new ComputerUseException(ErrorCodes.HostWindowForbidden, "click_control is forbidden on HostWindow.");

        var appKey = _identities.Resolve(token.Pid, token.CreateTimeUtc, token.ClassName).Value;

        if (!_catalog.TryLoadControl(appKey, controlId, out var control))
        {
            throw new ComputerUseException(
                ErrorCodes.UnknownControl,
                "The controlId is unknown for this AppKey.");
        }

        // 认屏/匹配必须对着本次 Capture 的 fitted 帧，禁止只吃 observe 缓存。
        var frame = await CaptureLiveFrameAsync(token, targetToken, cancellationToken).ConfigureAwait(false);
        if (frame.Bgra is not { Length: > 0 })
        {
            throw new ComputerUseException(
                ErrorCodes.EmptyFrame,
                "Capture produced an empty bitmap.");
        }

        var screens = _catalog.LoadAppScreens(appKey);
        var identified = ScreenIdentifier.Identify(
            frame.Bgra,
            frame.Width,
            frame.Height,
            frame.BgraStride,
            ScreenIdentifier.FromCatalog(screens),
            requiredScreenId: control.ScreenId);

        if (identified.Status != ScreenIdentifyStatus.Identified
            || !string.Equals(identified.ScreenId, control.ScreenId, StringComparison.Ordinal))
        {
            throw new ComputerUseException(
                ErrorCodes.ScreenMismatch,
                "The current frame is not the screen that owns this control.",
                new
                {
                    screenId = control.ScreenId,
                    identifiedScreenId = identified.ScreenId,
                    candidates = identified.CandidateIds,
                    status = identified.Status.ToString()
                });
        }

        cancellationToken.ThrowIfCancellationRequested();
        var match = MatchControl(frame, control);
        if (match.Status != TemplateMatchStatus.Found)
        {
            throw new ComputerUseException(
                MatchErrorCode(match.Status),
                MatchErrorMessage(match.Status),
                new
                {
                    screenId = control.ScreenId,
                    score = match.Score,
                    secondScore = match.SecondScore
                });
        }

        var centerX = match.X + match.Width / 2;
        var centerY = match.Y + match.Height / 2;
        var point = CoordinateMapper.MapImageToScreen(frame, centerX, centerY);

        Revalidate(token);
        Activate(token);
        var monitors = _monitors.EnumerateMonitors();
        HitTest(point, token, monitors);

        MoveTo(point);
        tracker.MouseDown(MouseButtonKind.Left);
        tracker.MouseUp(MouseButtonKind.Left);

        return new
        {
            controlId = control.ControlId,
            screenId = control.ScreenId,
            frameId = frame.FrameId,
            match = new
            {
                x = match.X,
                y = match.Y,
                width = match.Width,
                height = match.Height,
                score = match.Score
            }
        };
    }

    private TemplateMatchResult MatchControl(FrameRecord frame, CatalogControl control)
    {
        var templateStride = checked(control.Width * 4);
        if (control.Width <= 0
            || control.Height <= 0
            || control.Bgra is null
            || control.Bgra.Length < checked(templateStride * control.Height))
        {
            return new TemplateMatchResult(TemplateMatchStatus.NotFound, 0, 0, 0, 0, 0, 0);
        }

        var box = DenormalizeBox(control, frame.Width, frame.Height);
        var roi = ExpandRoi(box.X, box.Y, box.Width, box.Height, frame.Width, frame.Height);
        var roiIsFullFrame = roi.X == 0 && roi.Y == 0 && roi.Width == frame.Width && roi.Height == frame.Height;
        var roiLargeEnough = roi.Width >= control.Width && roi.Height >= control.Height;

        if (roiLargeEnough)
        {
            var roiMatch = MatchHaystack(frame, roi, control, templateStride);
            if (roiMatch.Status == TemplateMatchStatus.Found)
                return Offset(roiMatch, roi.X, roi.Y);
            if (roiIsFullFrame)
                return roiMatch;
        }

        var full = new SearchRect(0, 0, frame.Width, frame.Height);
        return MatchHaystack(frame, full, control, templateStride);
    }

    private TemplateMatchResult MatchHaystack(
        FrameRecord frame,
        SearchRect roi,
        CatalogControl control,
        int templateStride)
    {
        byte[] haystack;
        int hayWidth;
        int hayHeight;
        int hayStride;
        if (roi.X == 0 && roi.Y == 0 && roi.Width == frame.Width && roi.Height == frame.Height)
        {
            haystack = frame.Bgra!;
            hayWidth = frame.Width;
            hayHeight = frame.Height;
            hayStride = frame.BgraStride;
        }
        else
        {
            haystack = CropPacked(frame.Bgra!, frame.BgraStride, roi.X, roi.Y, roi.Width, roi.Height);
            hayWidth = roi.Width;
            hayHeight = roi.Height;
            hayStride = checked(roi.Width * 4);
        }

        return ZnccMatcher.Match(
            haystack,
            hayWidth,
            hayHeight,
            hayStride,
            control.Bgra,
            control.Width,
            control.Height,
            templateStride,
            _limits.TemplateScaleMin,
            _limits.TemplateScaleMax);
    }

    private async Task<FrameRecord> CaptureLiveFrameAsync(
        TargetTokenPayload token,
        string targetToken,
        CancellationToken cancellationToken)
    {
        CapturedBitmap? captured = null;
        try
        {
            // 与 observe/screenshot 相同：先 restore 再拍，避免最小化窗黑屏后误报认屏/匹配失败。
            _activator.RestoreIfMinimized(token.Hwnd, TimeSpan.FromMilliseconds(_limits.RestoreTimeoutMs));
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
                Rounding = CoordinateMapper.Rounding,
                Bgra = fitted.Bgra,
                BgraStride = fitted.Width * 4,
                ImageReturnedToClient = false
            };
            // 不写入 FrameCache：热路径每点一次都会挤掉仍可能用于 remember 的可视化帧。
            _frames.EnsureMatchesToken(frame, token);
            // 几何复核针对本次 Capture 后的 live 几何，而不是旧 observe 帧。
            _frames.EnsureGeometryIfPointer(frame, ReadGeometry(token.Hwnd), hasPointerActions: true);
            return frame;
        }
        finally
        {
            captured?.Return();
        }
    }

    // 与 OperateService.MoveTo 同语义：落点超 epsilon 则禁止 down/up。
    private void MoveTo(ScreenPoint point)
    {
        _input.MoveAbsoluteVirtualDesk(point.X, point.Y);
        var now = _input.GetCursorPos();
        if (Math.Abs(now.X - point.X) > _limits.InputPositionEpsilonPx
            || Math.Abs(now.Y - point.Y) > _limits.InputPositionEpsilonPx)
        {
            throw new ComputerUseException(
                ErrorCodes.InputPositionMismatch,
                "The pointer did not land on the requested physical pixel.");
        }
    }

    private void Activate(TargetTokenPayload token)
    {
        _activator.RestoreIfMinimized(token.Hwnd, TimeSpan.FromMilliseconds(_limits.RestoreTimeoutMs));
        if (!_activator.TryActivate(token.Hwnd)
            || !AccessGuards.ForegroundBelongsToTarget(_windows, _activator.GetForegroundWindow(), token.Hwnd, token.Pid))
        {
            throw new ComputerUseException(ErrorCodes.ActivationFailed, "The target window could not be activated.");
        }
    }

    private void HitTest(ScreenPoint point, TargetTokenPayload token, IReadOnlyList<MonitorInfo> monitors)
    {
        if (!_monitors.IsInAnyWorkArea(point, monitors))
            throw new ComputerUseException(ErrorCodes.PointOffscreen, "The point is outside every monitor work area.");
        var hit = _hitTester.WindowFromPhysicalPoint(point.X, point.Y);
        if (!AccessGuards.HitIsAllowed(_windows, hit, token.Hwnd))
            throw new ComputerUseException(ErrorCodes.PointOccluded, "The point is occluded or is not the target window.");
    }

    private void Revalidate(TargetTokenPayload token)
    {
        if (!TargetTokenService.MatchesLive(token, _windows, _processes))
            throw new ComputerUseException(ErrorCodes.StaleTarget, "The target token no longer matches a live window identity.");
    }

    private WindowGeometry ReadGeometry(nint hwnd) => new()
    {
        WindowRect = _windows.GetWindowRect(hwnd),
        ExtendedFrameBounds = _windows.GetExtendedFrameBounds(hwnd),
        Dpi = _windows.GetDpi(hwnd)
    };

    private static SearchRect DenormalizeBox(CatalogControl control, int frameWidth, int frameHeight)
    {
        var x = (int)Math.Floor(control.Nx * frameWidth);
        var y = (int)Math.Floor(control.Ny * frameHeight);
        var width = Math.Max(1, (int)Math.Round(control.Nw * frameWidth));
        var height = Math.Max(1, (int)Math.Round(control.Nh * frameHeight));
        x = Math.Clamp(x, 0, Math.Max(0, frameWidth - 1));
        y = Math.Clamp(y, 0, Math.Max(0, frameHeight - 1));
        width = Math.Min(width, frameWidth - x);
        height = Math.Min(height, frameHeight - y);
        return new SearchRect(x, y, Math.Max(0, width), Math.Max(0, height));
    }

    private static SearchRect ExpandRoi(int x, int y, int width, int height, int frameWidth, int frameHeight)
    {
        var padX = (int)Math.Ceiling(width * RoiExpandFactor / 2.0);
        var padY = (int)Math.Ceiling(height * RoiExpandFactor / 2.0);
        var left = Math.Clamp(x - padX, 0, frameWidth);
        var top = Math.Clamp(y - padY, 0, frameHeight);
        var right = Math.Clamp(x + width + padX, 0, frameWidth);
        var bottom = Math.Clamp(y + height + padY, 0, frameHeight);
        return new SearchRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static byte[] CropPacked(byte[] bgra, int stride, int x, int y, int cropWidth, int cropHeight)
    {
        var rowBytes = checked(cropWidth * 4);
        var dest = new byte[checked(rowBytes * cropHeight)];
        var srcOrigin = checked(y * stride + x * 4);
        for (var row = 0; row < cropHeight; row++)
        {
            Buffer.BlockCopy(
                bgra,
                checked(srcOrigin + row * stride),
                dest,
                checked(row * rowBytes),
                rowBytes);
        }

        return dest;
    }

    private static TemplateMatchResult Offset(TemplateMatchResult match, int originX, int originY) =>
        new(match.Status, match.X + originX, match.Y + originY, match.Width, match.Height, match.Score, match.SecondScore);

    private static string MatchErrorCode(TemplateMatchStatus status) => status switch
    {
        TemplateMatchStatus.Ambiguous => ErrorCodes.TemplateAmbiguous,
        TemplateMatchStatus.ScaleMismatch => ErrorCodes.TemplateScaleMismatch,
        _ => ErrorCodes.TemplateNotFound
    };

    private static string MatchErrorMessage(TemplateMatchStatus status) => status switch
    {
        TemplateMatchStatus.Ambiguous => "The control template matched more than one location.",
        TemplateMatchStatus.ScaleMismatch => "The control template scale is outside the allowed range.",
        _ => "The control template was not found on the current frame."
    };

    private readonly record struct SearchRect(int X, int Y, int Width, int Height);
}

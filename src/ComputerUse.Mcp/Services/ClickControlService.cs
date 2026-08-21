using ComputerUse.Mcp.Abstractions;
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
    private const double MaxFingerprintMae = 16;

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
        _host = host;
        _catalog = catalog;
        _limits = limits;
        _identities = identities;
    }

    public Task<object> ClickAsync(string targetToken, string controlId, string? operationId, CancellationToken cancellationToken) =>
        _coordinator.RunAsync(ct => ClickLockedAsync(targetToken, controlId, operationId, ct), cancellationToken);

    private Task<object> ClickLockedAsync(
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
                    return Task.FromResult(cached);
            }
        }

        var tracker = new InjectionTracker(_input);
        var mayHaveExecuted = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var body = ClickCore(targetToken, controlId, tracker, cancellationToken, out mayHaveExecuted);
            if (operationId is not null)
                _operationIds.Complete(operationId, body, true, false);
            return Task.FromResult(body);
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

    private object ClickCore(
        string targetToken,
        string controlId,
        InjectionTracker tracker,
        CancellationToken cancellationToken,
        out bool mayHaveExecuted)
    {
        mayHaveExecuted = false;
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

        if (!_frames.TryGetLatestForToken(token, out var frame) || frame.Bgra is not { Length: > 0 })
        {
            throw new ComputerUseException(
                ErrorCodes.StaleCapture,
                "No live frame with pixels is cached for this target token.");
        }

        _frames.EnsureMatchesToken(frame, token);
        _frames.EnsureGeometryIfPointer(frame, ReadGeometry(token.Hwnd), hasPointerActions: true);

        var screens = _catalog.LoadAppScreens(appKey);
        var library = ToLibrary(screens);
        var identified = ScreenIdentifier.Identify(
            frame.Bgra,
            frame.Width,
            frame.Height,
            frame.BgraStride,
            library,
            requiredScreenId: control.ScreenId);

        CatalogScreenAssets? required = null;
        foreach (var screen in screens)
        {
            if (string.Equals(screen.ScreenId, control.ScreenId, StringComparison.Ordinal))
            {
                required = screen;
                break;
            }
        }

        if (identified.Status != ScreenIdentifyStatus.Identified
            || !string.Equals(identified.ScreenId, control.ScreenId, StringComparison.Ordinal)
            || required is null
            || !FingerprintsAppearOnFrame(frame, required.Fingerprints))
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

        _input.MoveAbsoluteVirtualDesk(point.X, point.Y);
        tracker.MouseDown(MouseButtonKind.Left);
        tracker.MouseUp(MouseButtonKind.Left);
        mayHaveExecuted = true;

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

    private static IReadOnlyList<StoredScreenCatalogEntry> ToLibrary(IReadOnlyList<CatalogScreenAssets> screens)
    {
        var library = new StoredScreenCatalogEntry[screens.Count];
        for (var i = 0; i < screens.Count; i++)
        {
            var screen = screens[i];
            var fingerprints = new ScreenFingerprint[screen.Fingerprints.Count];
            for (var f = 0; f < screen.Fingerprints.Count; f++)
            {
                var fp = screen.Fingerprints[f];
                fingerprints[f] = new ScreenFingerprint(fp.X, fp.Y, fp.Width, fp.Height, fp.Bgra);
            }

            var controls = new StoredControlLayout[screen.Controls.Count];
            for (var c = 0; c < screen.Controls.Count; c++)
            {
                var stored = screen.Controls[c];
                controls[c] = new StoredControlLayout(stored.ControlId, stored.Nx, stored.Ny, stored.Nw, stored.Nh);
            }

            library[i] = new StoredScreenCatalogEntry(
                screen.ScreenId,
                new PerceptualHashValue(screen.PhashBits),
                fingerprints,
                controls);
        }

        return library;
    }

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

    // ZNCC/pHash ignore DC offset, so a different noise seed can still Identify; absolute MAE rejects that lookalike.
    private static bool FingerprintsAppearOnFrame(FrameRecord frame, IReadOnlyList<CatalogFingerprint> fingerprints)
    {
        if (fingerprints.Count == 0)
            return false;

        var required = fingerprints.Count <= 1 ? 1 : 2;
        var aligned = 0;
        foreach (var fingerprint in fingerprints)
        {
            if (FingerprintMae(frame, fingerprint) <= MaxFingerprintMae)
                aligned++;
        }

        return aligned >= required;
    }

    private static double FingerprintMae(FrameRecord frame, CatalogFingerprint fingerprint)
    {
        if (fingerprint.Bgra is null || fingerprint.Width <= 0 || fingerprint.Height <= 0)
            return double.PositiveInfinity;
        if (fingerprint.X < 0 || fingerprint.Y < 0
            || fingerprint.X + fingerprint.Width > frame.Width
            || fingerprint.Y + fingerprint.Height > frame.Height)
            return double.PositiveInfinity;

        var templateStride = checked(fingerprint.Width * 4);
        if (fingerprint.Bgra.Length < checked(templateStride * fingerprint.Height)
            || frame.Bgra is null)
            return double.PositiveInfinity;

        long total = 0;
        var count = 0;
        for (var row = 0; row < fingerprint.Height; row++)
        {
            var src = (fingerprint.Y + row) * frame.BgraStride + fingerprint.X * 4;
            var tmpl = row * templateStride;
            for (var x = 0; x < fingerprint.Width; x++)
            {
                var si = src + x * 4;
                var ti = tmpl + x * 4;
                total += Math.Abs(frame.Bgra[si] - fingerprint.Bgra[ti]);
                total += Math.Abs(frame.Bgra[si + 1] - fingerprint.Bgra[ti + 1]);
                total += Math.Abs(frame.Bgra[si + 2] - fingerprint.Bgra[ti + 2]);
                count += 3;
            }
        }

        return count == 0 ? double.PositiveInfinity : total / (double)count;
    }

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

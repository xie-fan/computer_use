using ComputerUse.Mcp.Abstractions;
using ComputerUse.Mcp.Coordination;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Identity;
using Microsoft.Extensions.Logging;

namespace ComputerUse.Mcp.Services;

internal sealed class OperateService
{
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
    private readonly IClipboardWorker _clipboard;
    private readonly IHostProcessResolver _host;
    private readonly WindowListService _list;
    private readonly Limits _limits;
    private readonly ILogger<OperateService> _logger;

    public OperateService(
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
        IClipboardWorker clipboard,
        IHostProcessResolver host,
        WindowListService list,
        Limits limits,
        ILogger<OperateService> logger)
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
        _clipboard = clipboard;
        _host = host;
        _list = list;
        _limits = limits;
        _logger = logger;
    }

    public Task<object> ExecuteAsync(ParsedOperateRequest request, CancellationToken cancellationToken) =>
        _coordinator.RunAsync(ct => ExecuteLockedAsync(request, ct), cancellationToken);

    private async Task<object> ExecuteLockedAsync(ParsedOperateRequest request, CancellationToken cancellationToken)
    {
        if (request.OperationId is not null)
        {
            var existing = _operationIds.TryBegin(request.OperationId);
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

        var side = new SideEffects();
        var warnings = new List<WarningItem>();
        var completed = 0;
        var mayHaveExecuted = false;
        var outcomeKnown = true;
        string? failCode = null;
        int? failedIndex = null;
        var tracker = new InjectionTracker(_input);
        object body;
        try
        {
            var token = _tokens.RequireValid(request.TargetToken, _windows, _processes);
            var frame = _frames.Require(request.FrameId);
            _frames.EnsureMatchesToken(frame, token);
            var live = ReadGeometry(token.Hwnd);
            _frames.EnsureGeometryIfPointer(frame, live, request.HasPointerActions);

            AccessGuards.EnsureInteractive(_session);
            AccessGuards.EnsureCurrentDesktop(_desktops, token.Hwnd);
            AccessGuards.EnsureIntegrity(_processes, token.Pid);
            if (_host.IsHostProcess(token.Pid))
                throw new ComputerUseException(ErrorCodes.HostWindowForbidden, "operate_window is forbidden on HostWindow.");

            var fgBefore = _activator.GetForegroundWindow();
            var restore = _activator.RestoreIfMinimized(token.Hwnd, TimeSpan.FromMilliseconds(_limits.RestoreTimeoutMs));
            side.WindowRestored = restore.PostedRestore;
            if (!_activator.TryActivate(token.Hwnd)
                || !AccessGuards.ForegroundBelongsToTarget(_windows, _activator.GetForegroundWindow(), token.Hwnd, token.Pid))
            {
                throw new ComputerUseException(ErrorCodes.ActivationFailed, "The target window could not be activated.");
            }
            side.ForegroundChanged = _activator.GetForegroundWindow() != fgBefore;

            var monitors = _monitors.EnumerateMonitors();
            for (var i = 0; i < request.Actions.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var action = request.Actions[i];
                try
                {
                    await ExecuteOneAsync(action, token, frame, monitors, tracker, warnings, cancellationToken).ConfigureAwait(false);
                    mayHaveExecuted = true;
                    completed++;
                    if (action is KeyAction { IsAltF4Terminator: true })
                        _tokens.Revoke(request.TargetToken);
                    if (i < request.Actions.Count - 1 && action is not WaitAction && request.PauseMs > 0)
                        await Task.Delay(request.PauseMs, cancellationToken).ConfigureAwait(false);
                }
                catch (ComputerUseException ex)
                {
                    failCode = ex.Code;
                    failedIndex = i;
                    outcomeKnown = true;
                    throw new ComputerUseException(ex.Code, ex.Message, BuildDetails(completed, failedIndex, true, mayHaveExecuted, side, warnings, ex.Code));
                }
            }

            body = BuildBody(completed, null, true, false, null, side, warnings);
            if (request.OperationId is not null)
                _operationIds.Complete(request.OperationId, body, true, false);
        }
        catch (ComputerUseException ex)
        {
            if (request.OperationId is not null)
                _operationIds.Complete(request.OperationId, ex.Details ?? BuildDetails(completed, failedIndex, true, mayHaveExecuted, side, warnings, ex.Code), true, true, ex.Code, ex.Message);
            throw;
        }
        catch (OperationCanceledException)
        {
            outcomeKnown = false;
            mayHaveExecuted = completed > 0 || mayHaveExecuted;
            var code = cancellationToken.IsCancellationRequested ? ErrorCodes.Cancelled : ErrorCodes.Timeout;
            var details = BuildDetails(completed, failedIndex ?? completed, outcomeKnown, mayHaveExecuted, side, warnings, code);
            if (request.OperationId is not null)
                _operationIds.Complete(request.OperationId, details, false, true, code, "The operate request did not finish.");
            throw new ComputerUseException(code, "The operate request did not finish.", details);
        }
        finally
        {
            tracker.ReleaseAll();
        }
        _logger.LogInformation("tool={Tool} code={Code} actionIndex={Index} completed={Completed}", "operate_window", failCode ?? "ok", failedIndex, completed);
        return body;
    }

    private async Task ExecuteOneAsync(
        WindowAction action,
        TargetTokenPayload token,
        FrameRecord frame,
        IReadOnlyList<MonitorInfo> monitors,
        InjectionTracker tracker,
        List<WarningItem> warnings,
        CancellationToken cancellationToken)
    {
        AccessGuards.EnsureInteractive(_session);
        Revalidate(token);
        EnsureFocus(token);

        switch (action)
        {
            case ClickAction click:
                await ClickAsync(click, token, frame, monitors, tracker, cancellationToken).ConfigureAwait(false);
                break;
            case MoveAction move:
                Move(move, frame, tracker);
                break;
            case ButtonDownAction down:
                PointerDown(down, token, frame, monitors, tracker);
                break;
            case ButtonUpAction up:
                PointerUp(up, token, frame, monitors, tracker);
                break;
            case ScrollAction scroll:
                Scroll(scroll, token, frame, monitors, tracker);
                break;
            case KeyAction key:
                Key(key, tracker);
                break;
            case TextAction text:
                Text(text.Value, tracker);
                break;
            case PasteAction paste:
                await PasteAsync(paste.Value, token, warnings, cancellationToken).ConfigureAwait(false);
                break;
            case WaitAction wait:
                await Task.Delay(wait.Ms, cancellationToken).ConfigureAwait(false);
                AccessGuards.EnsureInteractive(_session);
                EnsureFocus(token);
                break;
            default:
                throw new ComputerUseException(ErrorCodes.InvalidAction, "Unsupported action.");
        }
    }

    private async Task ClickAsync(ClickAction click, TargetTokenPayload token, FrameRecord frame, IReadOnlyList<MonitorInfo> monitors, InjectionTracker tracker, CancellationToken cancellationToken)
    {
        var point = CoordinateMapper.MapImageToScreen(frame, click.X, click.Y);
        for (var n = 0; n < click.Count; n++)
        {
            HitTest(point, token, monitors);
            MoveTo(point, tracker);
            tracker.MouseDown(click.Button);
            tracker.MouseUp(click.Button);
            if (n + 1 < click.Count)
                await Task.Delay(Math.Max(1, _input.DoubleClickTimeMs / 2), cancellationToken).ConfigureAwait(false);
        }
    }

    private void Move(MoveAction move, FrameRecord frame, InjectionTracker tracker)
    {
        var point = CoordinateMapper.MapImageToScreen(frame, move.X, move.Y);
        MoveTo(point, tracker);
    }

    private void PointerDown(ButtonDownAction down, TargetTokenPayload token, FrameRecord frame, IReadOnlyList<MonitorInfo> monitors, InjectionTracker tracker)
    {
        var point = ResolveOptionalPoint(down.X, down.Y, frame);
        HitTest(point, token, monitors);
        if (down.X is not null)
            MoveTo(point, tracker);
        tracker.MouseDown(down.Button);
    }

    private void PointerUp(ButtonUpAction up, TargetTokenPayload token, FrameRecord frame, IReadOnlyList<MonitorInfo> monitors, InjectionTracker tracker)
    {
        var point = ResolveOptionalPoint(up.X, up.Y, frame);
        HitTest(point, token, monitors);
        if (up.X is not null)
            MoveTo(point, tracker);
        tracker.MouseUp(up.Button);
    }

    private void Scroll(ScrollAction scroll, TargetTokenPayload token, FrameRecord frame, IReadOnlyList<MonitorInfo> monitors, InjectionTracker tracker)
    {
        var point = CoordinateMapper.MapImageToScreen(frame, scroll.X, scroll.Y);
        HitTest(point, token, monitors);
        MoveTo(point, tracker);
        _input.Scroll(scroll.Dx, scroll.Dy);
    }

    private void Key(KeyAction key, InjectionTracker tracker)
    {
        var mods = key.Modifiers;
        void DownMod(string name, ushort vk)
        {
            if (mods.Contains(name, StringComparer.Ordinal))
                tracker.KeyDown(vk, false);
        }

        DownMod("Ctrl", 0x11);
        DownMod("Alt", 0x12);
        DownMod("Shift", 0x10);
        var vk = KeyWhitelist.VirtualKey(key.Key);
        tracker.KeyDown(vk, KeyWhitelist.IsExtendedKey(key.Key));
        tracker.KeyUp(vk, KeyWhitelist.IsExtendedKey(key.Key));
        if (mods.Contains("Shift", StringComparer.Ordinal)) tracker.KeyUp(0x10, false);
        if (mods.Contains("Alt", StringComparer.Ordinal)) tracker.KeyUp(0x12, false);
        if (mods.Contains("Ctrl", StringComparer.Ordinal)) tracker.KeyUp(0x11, false);
    }

    private void Text(string value, InjectionTracker tracker)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch == '\r')
            {
                if (i + 1 < value.Length && value[i + 1] == '\n')
                    i++;
                tracker.KeyDown(0x0D, false);
                tracker.KeyUp(0x0D, false);
                continue;
            }
            if (ch == '\n')
            {
                tracker.KeyDown(0x0D, false);
                tracker.KeyUp(0x0D, false);
                continue;
            }
            tracker.UnicodeDown(ch);
            tracker.UnicodeUp(ch);
        }
    }

    private async Task PasteAsync(string value, TargetTokenPayload token, List<WarningItem> warnings, CancellationToken cancellationToken)
    {
        var result = await _clipboard.PasteUnicodeAsync(
            value,
            () => AccessGuards.ForegroundBelongsToTarget(_windows, _activator.GetForegroundWindow(), token.Hwnd, token.Pid),
            _limits.ClipboardRestoreWaitMs,
            cancellationToken).ConfigureAwait(false);
        if (result.Failed)
            throw new ComputerUseException(ErrorCodes.ClipboardFailed, result.Message ?? "Paste failed.");
        if (result.WarningCode is not null)
        {
            warnings.Add(new WarningItem
            {
                Code = result.WarningCode,
                Message = result.Message ?? result.WarningCode
            });
        }
    }

    private ScreenPoint ResolveOptionalPoint(int? x, int? y, FrameRecord frame)
    {
        if (x is not null && y is not null)
            return CoordinateMapper.MapImageToScreen(frame, x.Value, y.Value);
        return _input.GetCursorPos();
    }

    private void MoveTo(ScreenPoint point, InjectionTracker tracker)
    {
        _input.MoveAbsoluteVirtualDesk(point.X, point.Y);
        var now = _input.GetCursorPos();
        if (Math.Abs(now.X - point.X) > _limits.InputPositionEpsilonPx
            || Math.Abs(now.Y - point.Y) > _limits.InputPositionEpsilonPx)
        {
            throw new ComputerUseException(ErrorCodes.InputPositionMismatch, "The pointer did not land on the requested physical pixel.");
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
        if (!_windows.IsWindow(token.Hwnd)
            || _windows.GetPid(token.Hwnd) != token.Pid
            || !string.Equals(_windows.GetClassName(token.Hwnd), token.ClassName, StringComparison.Ordinal)
            || !_processes.TryGetCreateTimeUtc(token.Pid, out var createTime)
            || createTime != token.CreateTimeUtc)
        {
            throw new ComputerUseException(ErrorCodes.StaleTarget, "The target token no longer matches a live window identity.");
        }
    }

    private void EnsureFocus(TargetTokenPayload token)
    {
        if (!AccessGuards.ForegroundBelongsToTarget(_windows, _activator.GetForegroundWindow(), token.Hwnd, token.Pid))
            throw new ComputerUseException(ErrorCodes.FocusLost, "The target window is no longer in the foreground.");
    }

    private WindowGeometry ReadGeometry(nint hwnd) => new()
    {
        WindowRect = _windows.GetWindowRect(hwnd),
        ExtendedFrameBounds = _windows.GetExtendedFrameBounds(hwnd),
        Dpi = _windows.GetDpi(hwnd)
    };

    private object BuildBody(int completed, int? failedIndex, bool outcomeKnown, bool mayHaveExecuted, string? code, SideEffects side, IReadOnlyList<WarningItem> warnings) =>
        new
        {
            completedCount = completed,
            failedIndex,
            outcomeKnown,
            mayHaveExecuted,
            code,
            warnings,
            sideEffects = side,
            contractVersion = Contract.Version,
            serverVersion = Contract.ServerVersion,
            capabilities = _list.Capabilities(),
            limits = _limits.ToPublicDto()
        };

    private object BuildDetails(int completed, int? failedIndex, bool outcomeKnown, bool mayHaveExecuted, SideEffects side, IReadOnlyList<WarningItem> warnings, string code) =>
        BuildBody(completed, failedIndex, outcomeKnown, mayHaveExecuted, code, side, warnings);

    private sealed class InjectionTracker(IInputInjector input)
    {
        private readonly Stack<Pressed> _down = new();

        public void MouseDown(MouseButtonKind button)
        {
            input.MouseButton(button, true);
            _down.Push(Pressed.Mouse(button));
        }

        public void MouseUp(MouseButtonKind button)
        {
            input.MouseButton(button, false);
            RemoveLast(Pressed.Mouse(button));
        }

        public void KeyDown(ushort vk, bool extended)
        {
            input.Key(vk, true, extended);
            _down.Push(Pressed.Key(vk, extended));
        }

        public void KeyUp(ushort vk, bool extended)
        {
            input.Key(vk, false, extended);
            RemoveLast(Pressed.Key(vk, extended));
        }

        public void UnicodeDown(char ch)
        {
            input.Unicode(ch, true);
            _down.Push(Pressed.Uni(ch));
        }

        public void UnicodeUp(char ch)
        {
            input.Unicode(ch, false);
            RemoveLast(Pressed.Uni(ch));
        }

        public void ReleaseAll()
        {
            while (_down.Count > 0)
            {
                var item = _down.Pop();
                try
                {
                    switch (item.Kind)
                    {
                        case PressKind.Mouse:
                            input.MouseButton((MouseButtonKind)item.Code, false);
                            break;
                        case PressKind.Key:
                            input.Key((ushort)item.Code, false, item.Extended);
                            break;
                        case PressKind.Unicode:
                            input.Unicode((char)item.Code, false);
                            break;
                    }
                }
                catch
                {
                    // never throw from finally cleanup
                }
            }
        }

        private void RemoveLast(Pressed match)
        {
            if (_down.Count == 0)
                return;
            var tmp = new Stack<Pressed>();
            var removed = false;
            while (_down.Count > 0)
            {
                var item = _down.Pop();
                if (!removed && item == match)
                {
                    removed = true;
                    continue;
                }
                tmp.Push(item);
            }
            while (tmp.Count > 0)
                _down.Push(tmp.Pop());
        }

        private readonly record struct Pressed(PressKind Kind, int Code, bool Extended)
        {
            public static Pressed Mouse(MouseButtonKind b) => new(PressKind.Mouse, (int)b, false);
            public static Pressed Key(ushort vk, bool ext) => new(PressKind.Key, vk, ext);
            public static Pressed Uni(char ch) => new(PressKind.Unicode, ch, false);
        }

        private enum PressKind { Mouse, Key, Unicode }
    }
}

using ComputerUse.Mcp.Abstractions;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Identity;

namespace ComputerUse.Mcp.Services;

internal sealed class WindowListService
{
    private static readonly HashSet<string> ExcludedClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "tooltips_class32",
        "NotifyIconOverflowWindow",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "TaskListThumbnailWnd",
        "TaskListOverlayWnd",
        "SysShadow",
        "IME",
        "MSCTFIME UI"
    };

    private readonly IWindowQuery _windows;
    private readonly IMonitorQuery _monitors;
    private readonly IProcessQuery _processes;
    private readonly IVirtualDesktopMembership _desktops;
    private readonly IHostProcessResolver _host;
    private readonly TargetTokenService _tokens;
    private readonly Limits _limits;

    public WindowListService(
        IWindowQuery windows,
        IMonitorQuery monitors,
        IProcessQuery processes,
        IVirtualDesktopMembership desktops,
        IHostProcessResolver host,
        TargetTokenService tokens,
        Limits limits)
    {
        _windows = windows;
        _monitors = monitors;
        _processes = processes;
        _desktops = desktops;
        _host = host;
        _tokens = tokens;
        _limits = limits;
    }

    public object List()
    {
        var capturedAt = DateTimeOffset.UtcNow;
        var monitors = _monitors.EnumerateMonitors();
        var warnings = new List<WarningItem>();
        var windows = new List<object>();
        var processCache = new Dictionary<uint, ProcessInfo>();
        _host.RefreshHostTree();

        foreach (var hwnd in _windows.EnumTopLevelWindows())
        {
            if (windows.Count >= _limits.MaxListWindows)
            {
                warnings.Add(new WarningItem
                {
                    Code = "list_truncated",
                    Message = $"Listing stopped at maxListWindows ({_limits.MaxListWindows})."
                });
                break;
            }

            try
            {
                if (!TryDescribe(hwnd, monitors, warnings, processCache, out var dto))
                    continue;
                windows.Add(dto);
            }
            catch
            {
                // Enumerated window was destroyed; skip.
            }
        }

        return new
        {
            snapshotId = Guid.NewGuid().ToString("N"),
            capturedAt,
            contractVersion = Contract.Version,
            serverVersion = Contract.ServerVersion,
            capabilities = Capabilities(),
            limits = _limits.ToPublicDto(),
            monitors = monitors.Select(m => m.ToDto()).ToArray(),
            windows,
            warnings
        };
    }

    public object Capabilities() => new
    {
        virtualDesktop = new
        {
            membershipQuery = _desktops.MembershipQueryAvailable,
            switching = false
        }
    };

    private bool TryDescribe(
        nint hwnd,
        IReadOnlyList<MonitorInfo> monitors,
        List<WarningItem> warnings,
        Dictionary<uint, ProcessInfo> processCache,
        out object dto)
    {
        dto = null!;
        if (!_windows.IsWindow(hwnd))
            return false;
        if (_windows.GetParent(hwnd) != 0)
            return false;

        var className = _windows.GetClassName(hwnd);
        if (ExcludedClasses.Contains(className))
            return false;

        var title = _windows.GetTitle(hwnd);
        var ex = _windows.GetExStyle(hwnd);
        if (string.IsNullOrEmpty(title) && (ex & Native.NativeMethods.WS_EX_TOOLWINDOW) != 0)
            return false;

        var styleVisible = _windows.IsVisibleStyle(hwnd);
        var minimized = _windows.IsMinimized(hwnd);
        if (!styleVisible && !minimized)
            return false;

        var cloaked = false;
        _windows.TryGetCloaked(hwnd, out cloaked);
        if (cloaked && !minimized)
            return false;

        var pid = _windows.GetPid(hwnd);
        if (pid == 0)
            return false;
        if (!processCache.TryGetValue(pid, out var info))
        {
            if (!_processes.TryGetInfo(pid, out info))
                return false;
            processCache[pid] = info;
        }

        var token = _tokens.Issue(hwnd, pid, info.CreateTimeUtc, className);
        var processName = info.ProcessName;
        if (processName is null)
        {
            warnings.Add(new WarningItem
            {
                Code = "process_name_unavailable",
                Message = "Process name could not be resolved.",
                Details = new { pid, targetToken = token }
            });
        }

        var bounds = _windows.GetWindowRect(hwnd);
        var monitor = _monitors.FromWindow(hwnd, monitors);
        var onCurrent = _desktops.IsOnCurrentVirtualDesktop(hwnd, out var desktopId);

        dto = new
        {
            targetToken = token,
            hwnd = TargetTokenService.FormatHwnd(hwnd),
            title,
            pid,
            processName,
            className,
            bounds = bounds.ToDto(),
            monitor = new { deviceName = monitor?.DeviceName },
            styleVisible,
            minimized,
            cloaked,
            effectiveVisible = styleVisible && !minimized && !cloaked,
            onCurrentVirtualDesktop = onCurrent,
            virtualDesktopId = desktopId,
            isHostWindow = _host.IsHostProcess(pid, info.CreateTimeUtc),
            integrityBlocked = AccessGuards.IntegrityBlocked(info.Integrity, _processes.GetCurrentIntegrityLevel())
        };
        return true;
    }
}

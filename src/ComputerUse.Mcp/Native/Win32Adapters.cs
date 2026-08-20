using System.Runtime.InteropServices;
using System.Text;
using ComputerUse.Mcp.Abstractions;
using ComputerUse.Mcp.Domain;

namespace ComputerUse.Mcp.Native;

internal sealed class Win32WindowQuery : IWindowQuery
{
    public bool IsWindow(nint hwnd) => NativeMethods.IsWindow(hwnd);

    public uint GetPid(nint hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        return pid;
    }

    public string GetClassName(nint hwnd)
    {
        var sb = new StringBuilder(256);
        var n = NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
        return n <= 0 ? "" : sb.ToString();
    }

    public string GetTitle(nint hwnd)
    {
        var len = NativeMethods.GetWindowTextLength(hwnd);
        if (len <= 0)
            return "";
        var sb = new StringBuilder(len + 1);
        NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public ScreenRect GetWindowRect(nint hwnd)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out var r))
            return new ScreenRect(0, 0, 0, 0);
        return FromRect(r);
    }

    public ScreenRect GetExtendedFrameBounds(nint hwnd)
    {
        if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out NativeMethods.RECT r, Marshal.SizeOf<NativeMethods.RECT>()) == 0)
            return FromRect(r);
        return GetWindowRect(hwnd);
    }

    public Dpi GetDpi(nint hwnd)
    {
        var dpi = NativeMethods.GetDpiForWindow(hwnd);
        if (dpi == 0)
            return Dpi.Default;
        return new Dpi(dpi, dpi);
    }

    public bool IsVisibleStyle(nint hwnd) =>
        (unchecked((int)NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE)) & NativeMethods.WS_VISIBLE) != 0;

    public bool IsMinimized(nint hwnd) => NativeMethods.IsIconic(hwnd);

    public bool TryGetCloaked(nint hwnd, out bool cloaked)
    {
        cloaked = false;
        if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out int value, sizeof(int)) != 0)
            return false;
        cloaked = value != 0;
        return true;
    }

    public nint GetOwner(nint hwnd) => NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER);
    public nint GetAncestorRoot(nint hwnd) => NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
    public nint GetAncestorRootOwner(nint hwnd) => NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOTOWNER);
    public nint GetParent(nint hwnd) => NativeMethods.GetParent(hwnd);
    public int GetStyle(nint hwnd) => unchecked((int)NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE));
    public int GetExStyle(nint hwnd) => unchecked((int)NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE));
    public nint MonitorFromWindowHandle(nint hwnd) => NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);

    public IReadOnlyList<nint> EnumTopLevelWindows()
    {
        var list = new List<nint>(64);
        NativeMethods.EnumWindows((h, _) =>
        {
            list.Add(h);
            return true;
        }, 0);
        return list;
    }

    internal static ScreenRect FromRect(NativeMethods.RECT r) =>
        new(r.Left, r.Top, Math.Max(0, r.Width), Math.Max(0, r.Height));
}

internal sealed class Win32MonitorQuery : IMonitorQuery
{
    public IReadOnlyList<MonitorInfo> EnumerateMonitors()
    {
        var list = new List<MonitorInfo>();
        NativeMethods.EnumDisplayMonitors(0, 0, (hMon, _, _, _) =>
        {
            var info = new NativeMethods.MONITORINFOEX { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
            if (!NativeMethods.GetMonitorInfo(hMon, ref info))
                return true;
            uint dpiX = 96, dpiY = 96;
            _ = NativeMethods.GetDpiForMonitor(hMon, NativeMethods.MDT_EFFECTIVE_DPI, out dpiX, out dpiY);
            list.Add(new MonitorInfo
            {
                DeviceName = string.IsNullOrWhiteSpace(info.szDevice) ? $"MONITOR_{list.Count}" : info.szDevice,
                Primary = (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0,
                Bounds = Win32WindowQuery.FromRect(info.rcMonitor),
                WorkArea = Win32WindowQuery.FromRect(info.rcWork),
                Dpi = new Dpi(dpiX, dpiY),
                Index = list.Count,
                Handle = hMon
            });
            return true;
        }, 0);
        return list;
    }

    public MonitorInfo? FromWindow(nint hwnd, IReadOnlyList<MonitorInfo> snapshot)
    {
        var handle = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        return snapshot.FirstOrDefault(m => m.Handle == handle) ?? snapshot.FirstOrDefault(m => m.Primary);
    }

    public bool IsInAnyWorkArea(ScreenPoint point, IReadOnlyList<MonitorInfo> snapshot)
    {
        foreach (var m in snapshot)
        {
            if (point.X >= m.WorkArea.Left && point.X < m.WorkArea.Right
                && point.Y >= m.WorkArea.Top && point.Y < m.WorkArea.Bottom)
            {
                return true;
            }
        }
        return false;
    }
}

internal sealed class Win32ProcessQuery : IProcessQuery
{
    private readonly IntegrityLevel _current;

    public Win32ProcessQuery()
    {
        _current = ReadIntegrity(NativeMethods.GetCurrentProcessId());
    }

    public IntegrityLevel GetCurrentIntegrityLevel() => _current;

    public bool TryGetCreateTimeUtc(uint pid, out long fileTimeUtc)
    {
        fileTimeUtc = 0;
        var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == 0)
            return false;
        try
        {
            if (!NativeMethods.GetProcessTimes(handle, out var creation, out _, out _, out _))
                return false;
            fileTimeUtc = creation;
            return true;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    public string? TryGetProcessName(uint pid)
    {
        var path = TryGetNormalizedImagePath(pid);
        if (path is null)
            return null;
        return Path.GetFileNameWithoutExtension(path);
    }

    public string? TryGetNormalizedImagePath(uint pid)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == 0)
            return null;
        try
        {
            var sb = new StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            if (!NativeMethods.QueryFullProcessImageName(handle, 0, sb, ref size))
                return null;
            try
            {
                return Path.GetFullPath(sb.ToString());
            }
            catch (Exception)
            {
                return sb.ToString();
            }
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    public uint? TryGetParentPid(uint pid)
    {
        var snap = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0);
        if (snap == nint.Zero || snap == (nint)(-1))
            return null;
        try
        {
            var pe = new NativeMethods.PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<NativeMethods.PROCESSENTRY32>() };
            if (!NativeMethods.Process32First(snap, ref pe))
                return null;
            do
            {
                if (pe.th32ProcessID == pid)
                    return pe.th32ParentProcessID;
            } while (NativeMethods.Process32Next(snap, ref pe));
            return null;
        }
        finally
        {
            NativeMethods.CloseHandle(snap);
        }
    }

    public IntegrityLevel GetIntegrityLevel(uint pid) => ReadIntegrity(pid);

    private static IntegrityLevel ReadIntegrity(uint pid)
    {
        var process = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0)
            return IntegrityLevel.Unknown;
        nint token = 0;
        nint buffer = 0;
        try
        {
            if (!NativeMethods.OpenProcessToken(process, NativeMethods.TOKEN_QUERY, out token))
                return IntegrityLevel.Unknown;
            NativeMethods.GetTokenInformation(token, NativeMethods.TokenIntegrityLevel, 0, 0, out var needed);
            if (needed <= 0)
                return IntegrityLevel.Unknown;
            buffer = Marshal.AllocHGlobal(needed);
            if (!NativeMethods.GetTokenInformation(token, NativeMethods.TokenIntegrityLevel, buffer, needed, out _))
                return IntegrityLevel.Unknown;
            var label = Marshal.PtrToStructure<NativeMethods.TOKEN_MANDATORY_LABEL>(buffer);
            var countPtr = NativeMethods.GetSidSubAuthorityCount(label.Sid);
            var count = Marshal.ReadByte(countPtr);
            var sub = NativeMethods.GetSidSubAuthority(label.Sid, (uint)(count - 1));
            var rid = Marshal.ReadInt32(sub);
            return rid switch
            {
                0x0000 => IntegrityLevel.Untrusted,
                0x1000 => IntegrityLevel.Low,
                0x2000 => IntegrityLevel.Medium,
                0x2100 => IntegrityLevel.MediumPlus,
                0x3000 => IntegrityLevel.High,
                0x4000 => IntegrityLevel.System,
                0x5000 => IntegrityLevel.Protected,
                _ => (IntegrityLevel)rid
            };
        }
        catch
        {
            return IntegrityLevel.Unknown;
        }
        finally
        {
            if (buffer != 0)
                Marshal.FreeHGlobal(buffer);
            if (token != 0)
                NativeMethods.CloseHandle(token);
            NativeMethods.CloseHandle(process);
        }
    }
}

internal sealed class Win32HitTester : IHitTester
{
    public nint WindowFromPhysicalPoint(int x, int y) =>
        NativeMethods.WindowFromPoint(new NativeMethods.POINT { X = x, Y = y });
}

internal sealed class Win32WindowActivator : IWindowActivator
{
    public nint GetForegroundWindow() => NativeMethods.GetForegroundWindow();

    public RestoreAttempt RestoreIfMinimized(nint hwnd, TimeSpan timeout)
    {
        var fgBefore = NativeMethods.GetForegroundWindow();
        if (!NativeMethods.IsIconic(hwnd))
        {
            return new RestoreAttempt(false, true, fgBefore, NativeMethods.GetForegroundWindow());
        }

        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        var deadline = DateTime.UtcNow + timeout;
        var restored = false;
        while (DateTime.UtcNow < deadline)
        {
            if (!NativeMethods.IsIconic(hwnd) && NativeMethods.GetWindowRect(hwnd, out var r) && r.Width > 0 && r.Height > 0)
            {
                restored = true;
                break;
            }
            Thread.Sleep(20);
        }

        return new RestoreAttempt(true, restored, fgBefore, NativeMethods.GetForegroundWindow());
    }

    public bool TryActivate(nint hwnd)
    {
        if (NativeMethods.IsIconic(hwnd))
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);

        NativeMethods.AllowSetForegroundWindow(unchecked((uint)-1));
        var fg = NativeMethods.GetForegroundWindow();
        var fgThread = NativeMethods.GetWindowThreadProcessId(fg, out _);
        var targetThread = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
        var current = NativeMethods.GetCurrentThreadId();
        var attachedFg = fgThread != 0 && fgThread != current && NativeMethods.AttachThreadInput(current, fgThread, true);
        var attachedTarget = targetThread != 0 && targetThread != current && NativeMethods.AttachThreadInput(current, targetThread, true);
        try
        {
            NativeMethods.BringWindowToTop(hwnd);
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
            return NativeMethods.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attachedTarget)
                NativeMethods.AttachThreadInput(current, targetThread, false);
            if (attachedFg)
                NativeMethods.AttachThreadInput(current, fgThread, false);
        }
    }
}

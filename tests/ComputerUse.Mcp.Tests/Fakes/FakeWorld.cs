using ComputerUse.Mcp.Abstractions;
using ComputerUse.Mcp.Domain;

namespace ComputerUse.Mcp.Tests.Fakes;

internal sealed class FakeWindow
{
    public nint Hwnd { get; init; }
    public uint Pid { get; set; }
    public string ClassName { get; set; } = "TestClass";
    public string Title { get; set; } = "Test";
    public ScreenRect WindowRect { get; set; } = new(0, 0, 200, 100);
    public ScreenRect ExtendedFrameBounds { get; set; } = new(0, 0, 200, 100);
    public Dpi Dpi { get; set; } = Dpi.Default;
    public bool Visible { get; set; } = true;
    public bool Minimized { get; set; }
    public bool Cloaked { get; set; }
    public nint Owner { get; set; }
    public nint Parent { get; set; }
    public int Style { get; set; } = 0x10000000;
    public int ExStyle { get; set; }
}

internal sealed class FakeProcess
{
    public uint Pid { get; init; }
    public uint? ParentPid { get; init; }
    public long CreateTimeUtc { get; set; }
    public string? Name { get; init; } = "app";
    public string? ImagePath { get; init; } = @"C:\apps\app.exe";
    public string? PackageFamilyName { get; init; }
    public string? SignerSubject { get; init; }
    public string? ProductName { get; init; }
    public string? ProductVersion { get; init; }
    public IntegrityLevel Integrity { get; init; } = IntegrityLevel.Medium;
}

internal sealed class FakeWorld : IWindowQuery, IProcessQuery
{
    public Dictionary<nint, FakeWindow> Windows { get; } = new();
    public Dictionary<uint, FakeProcess> Processes { get; } = new();
    public IntegrityLevel CurrentIntegrity { get; set; } = IntegrityLevel.Medium;

    public int InfoCallCount { get; private set; }
    public int ParentMapCallCount { get; private set; }
    public int PackageFamilyNameCallCount { get; private set; }
    public int SignerSubjectCallCount { get; private set; }
    public int ProductNameCallCount { get; private set; }
    public int ProductVersionCallCount { get; private set; }

    public bool IsWindow(nint hwnd) => Windows.ContainsKey(hwnd);
    public uint GetPid(nint hwnd) => Windows[hwnd].Pid;
    public string GetClassName(nint hwnd) => Windows[hwnd].ClassName;
    public string GetTitle(nint hwnd) => Windows[hwnd].Title;
    public ScreenRect GetWindowRect(nint hwnd) => Windows[hwnd].WindowRect;
    public ScreenRect GetExtendedFrameBounds(nint hwnd) => Windows[hwnd].ExtendedFrameBounds;
    public Dpi GetDpi(nint hwnd) => Windows[hwnd].Dpi;
    public bool IsVisibleStyle(nint hwnd) => Windows[hwnd].Visible;
    public bool IsMinimized(nint hwnd) => Windows[hwnd].Minimized;
    public bool TryGetCloaked(nint hwnd, out bool cloaked)
    {
        cloaked = Windows[hwnd].Cloaked;
        return true;
    }
    public nint GetOwner(nint hwnd) => Windows[hwnd].Owner;
    public nint GetAncestorRoot(nint hwnd) => hwnd;
    public nint GetAncestorRootOwner(nint hwnd) => Windows[hwnd].Owner == 0 ? hwnd : Windows[hwnd].Owner;
    public nint GetParent(nint hwnd) => Windows[hwnd].Parent;
    public int GetStyle(nint hwnd) => Windows[hwnd].Style;
    public int GetExStyle(nint hwnd) => Windows[hwnd].ExStyle;
    public nint MonitorFromWindowHandle(nint hwnd) => 1;
    public IReadOnlyList<nint> EnumTopLevelWindows() => Windows.Keys.ToList();

    public bool TryGetCreateTimeUtc(uint pid, out long fileTimeUtc)
    {
        if (Processes.TryGetValue(pid, out var p))
        {
            fileTimeUtc = p.CreateTimeUtc;
            return true;
        }
        fileTimeUtc = 0;
        return false;
    }

    public string? TryGetProcessName(uint pid) => Processes.TryGetValue(pid, out var p) ? p.Name : null;
    public string? TryGetNormalizedImagePath(uint pid) => Processes.TryGetValue(pid, out var p) ? p.ImagePath : null;
    public string? TryGetPackageFamilyName(uint pid)
    {
        PackageFamilyNameCallCount++;
        return Processes.TryGetValue(pid, out var p) ? BlankToNull(p.PackageFamilyName) : null;
    }
    public string? TryGetSignerSubject(uint pid)
    {
        SignerSubjectCallCount++;
        return Processes.TryGetValue(pid, out var p) ? BlankToNull(p.SignerSubject) : null;
    }
    public string? TryGetProductName(uint pid)
    {
        ProductNameCallCount++;
        return Processes.TryGetValue(pid, out var p) ? BlankToNull(p.ProductName) : null;
    }
    public string? TryGetProductVersion(uint pid)
    {
        ProductVersionCallCount++;
        return Processes.TryGetValue(pid, out var p) ? BlankToNull(p.ProductVersion) : null;
    }
    public uint? TryGetParentPid(uint pid) => Processes.TryGetValue(pid, out var p) ? p.ParentPid : null;
    public IntegrityLevel GetIntegrityLevel(uint pid) => Processes.TryGetValue(pid, out var p) ? p.Integrity : IntegrityLevel.Unknown;
    public IntegrityLevel GetCurrentIntegrityLevel() => CurrentIntegrity;

    public bool TryGetInfo(uint pid, out ProcessInfo info)
    {
        InfoCallCount++;
        if (!Processes.TryGetValue(pid, out var p))
        {
            info = default;
            return false;
        }

        info = new ProcessInfo(pid, p.CreateTimeUtc, p.ImagePath, p.Name, p.Integrity);
        return true;
    }

    public IReadOnlyDictionary<uint, uint> CaptureParentMap()
    {
        ParentMapCallCount++;
        return Processes.ToDictionary(kv => kv.Key, kv => kv.Value.ParentPid ?? 0u);
    }

    private static string? BlankToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}

internal sealed class FakeMonitors : IMonitorQuery
{
    public IReadOnlyList<MonitorInfo> Items { get; } =
    [
        new MonitorInfo
        {
            DeviceName = @"\\.\DISPLAY1",
            Primary = true,
            Bounds = new ScreenRect(0, 0, 1920, 1080),
            WorkArea = new ScreenRect(0, 0, 1920, 1040),
            Dpi = Dpi.Default,
            Index = 0,
            Handle = 1
        }
    ];

    public IReadOnlyList<MonitorInfo> EnumerateMonitors() => Items;
    public MonitorInfo? FromWindow(nint hwnd, IReadOnlyList<MonitorInfo> snapshot) => snapshot.Count > 0 ? snapshot[0] : null;
    public bool IsInAnyWorkArea(ScreenPoint point, IReadOnlyList<MonitorInfo> snapshot) => true;
}

internal sealed class FakeDesktops : IVirtualDesktopMembership
{
    public bool MembershipQueryAvailable => true;

    public bool? IsOnCurrentVirtualDesktop(nint hwnd, out Guid? desktopId)
    {
        desktopId = Guid.Empty;
        return true;
    }
}

internal sealed class StubHost : IHostProcessResolver
{
    public bool Result { get; set; }
    public int RefreshCount { get; private set; }

    public bool IsHostProcess(uint pid) => Result;
    public bool IsHostProcess(uint pid, long createTimeUtc) => Result;
    public void RefreshHostTree() => RefreshCount++;
}

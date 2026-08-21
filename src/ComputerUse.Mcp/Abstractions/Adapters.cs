using ComputerUse.Mcp.Domain;

namespace ComputerUse.Mcp.Abstractions;

internal interface IWindowQuery
{
    bool IsWindow(nint hwnd);
    uint GetPid(nint hwnd);
    string GetClassName(nint hwnd);
    string GetTitle(nint hwnd);
    ScreenRect GetWindowRect(nint hwnd);
    ScreenRect GetExtendedFrameBounds(nint hwnd);
    Dpi GetDpi(nint hwnd);
    bool IsVisibleStyle(nint hwnd);
    bool IsMinimized(nint hwnd);
    bool TryGetCloaked(nint hwnd, out bool cloaked);
    nint GetOwner(nint hwnd);
    nint GetAncestorRoot(nint hwnd);
    nint GetAncestorRootOwner(nint hwnd);
    nint GetParent(nint hwnd);
    int GetStyle(nint hwnd);
    int GetExStyle(nint hwnd);
    nint MonitorFromWindowHandle(nint hwnd);
    IReadOnlyList<nint> EnumTopLevelWindows();
}

internal interface IMonitorQuery
{
    IReadOnlyList<MonitorInfo> EnumerateMonitors();
    MonitorInfo? FromWindow(nint hwnd, IReadOnlyList<MonitorInfo> snapshot);
    bool IsInAnyWorkArea(ScreenPoint point, IReadOnlyList<MonitorInfo> snapshot);
}

internal interface IProcessQuery
{
    bool TryGetCreateTimeUtc(uint pid, out long fileTimeUtc);
    string? TryGetProcessName(uint pid);
    string? TryGetNormalizedImagePath(uint pid);
    string? TryGetPackageFamilyName(uint pid);
    string? TryGetSignerSubject(uint pid);
    string? TryGetProductName(uint pid);
    string? TryGetProductVersion(uint pid);
    uint? TryGetParentPid(uint pid);
    IntegrityLevel GetIntegrityLevel(uint pid);
    IntegrityLevel GetCurrentIntegrityLevel();
    bool TryGetInfo(uint pid, out ProcessInfo info);
    IReadOnlyDictionary<uint, uint> CaptureParentMap();
}

internal interface IVirtualDesktopMembership
{
    bool MembershipQueryAvailable { get; }
    bool? IsOnCurrentVirtualDesktop(nint hwnd, out Guid? desktopId);
}

internal interface ISessionGuard
{
    SessionDenial Evaluate();
}

internal interface IWindowActivator
{
    nint GetForegroundWindow();
    RestoreAttempt RestoreIfMinimized(nint hwnd, TimeSpan timeout);
    bool TryActivate(nint hwnd);
}

internal readonly record struct RestoreAttempt(bool PostedRestore, bool Restored, nint ForegroundBefore, nint ForegroundAfter);

internal interface IHitTester
{
    nint WindowFromPhysicalPoint(int x, int y);
}

internal interface IInputInjector
{
    bool SwapMouseButtons { get; }
    int DoubleClickTimeMs { get; }
    void RefreshMetrics();
    void MoveAbsoluteVirtualDesk(int physicalX, int physicalY);
    ScreenPoint GetCursorPos();
    void MouseButton(MouseButtonKind logicalButton, bool down);
    void Scroll(int dxNotches, int dyNotches);
    void Key(ushort virtualKey, bool down, bool extended);
    void KeyStroke(ushort virtualKey, bool extended, bool ctrl, bool alt, bool shift);
    void Unicode(char codeUnit, bool down);
    void UnicodeText(ReadOnlySpan<char> codeUnits);
}

internal interface IClipboardWorker
{
    Task<ClipboardPasteResult> PasteUnicodeAsync(
        string value,
        Func<bool> confirmForeground,
        int restoreWaitMs,
        CancellationToken cancellationToken);
}

internal readonly record struct ClipboardPasteResult(bool Restored, bool Failed, string? WarningCode, string? Message);

internal interface ICapturePipeline
{
    Task<CapturedBitmap> CaptureAsync(nint hwnd, int timeoutMs, CancellationToken cancellationToken);
}

internal interface IHostProcessResolver
{
    bool IsHostProcess(uint pid);
    bool IsHostProcess(uint pid, long createTimeUtc);
    void RefreshHostTree();
}

internal readonly record struct HostIdentity(uint Pid, long CreateTimeUtc, string? ImagePath);

internal readonly record struct ProcessInfo(
    uint Pid,
    long CreateTimeUtc,
    string? ImagePath,
    string? ProcessName,
    IntegrityLevel Integrity);

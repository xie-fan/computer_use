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
    uint? TryGetParentPid(uint pid);
    IntegrityLevel GetIntegrityLevel(uint pid);
    IntegrityLevel GetCurrentIntegrityLevel();
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
    void MoveAbsoluteVirtualDesk(int physicalX, int physicalY);
    ScreenPoint GetCursorPos();
    void MouseButton(MouseButtonKind logicalButton, bool down);
    void Scroll(int dxNotches, int dyNotches);
    void Key(ushort virtualKey, bool down, bool extended);
    void Unicode(char codeUnit, bool down);
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
}

internal readonly record struct HostIdentity(uint Pid, long CreateTimeUtc, string? ImagePath);

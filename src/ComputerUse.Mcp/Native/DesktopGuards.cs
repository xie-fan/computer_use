using System.Runtime.InteropServices;
using ComputerUse.Mcp.Abstractions;
using ComputerUse.Mcp.Domain;

namespace ComputerUse.Mcp.Native;

internal sealed class VirtualDesktopMembership : IVirtualDesktopMembership, IDisposable
{
    private readonly object? _manager;
    public bool MembershipQueryAvailable { get; }

    public VirtualDesktopMembership()
    {
        try
        {
            var clsid = NativeMethods.CLSID_VirtualDesktopManager;
            var iid = NativeMethods.IID_IVirtualDesktopManager;
            var hr = NativeMethods.CoCreateInstance(ref clsid, 0, NativeMethods.CLSCTX_INPROC_SERVER, ref iid, out var ptr);
            if (hr != 0 || ptr == 0)
                return;
            _manager = Marshal.GetObjectForIUnknown(ptr);
            Marshal.Release(ptr);
            MembershipQueryAvailable = _manager is IVirtualDesktopManagerNative;
        }
        catch (COMException)
        {
            MembershipQueryAvailable = false;
        }
    }

    public bool? IsOnCurrentVirtualDesktop(nint hwnd, out Guid? desktopId)
    {
        desktopId = null;
        if (_manager is not IVirtualDesktopManagerNative native)
            return null;
        try
        {
            var hr = native.IsWindowOnCurrentVirtualDesktop(hwnd, out var onCurrent);
            if (hr != 0)
                return null;
            var idHr = native.GetWindowDesktopId(hwnd, out var id);
            if (idHr == 0 && id != Guid.Empty)
                desktopId = id;
            return onCurrent != 0;
        }
        catch (COMException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_manager is not null)
            Marshal.FinalReleaseComObject(_manager);
    }
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
internal interface IVirtualDesktopManagerNative
{
    [PreserveSig]
    int IsWindowOnCurrentVirtualDesktop(nint topLevelWindow, out int onCurrentDesktop);

    [PreserveSig]
    int GetWindowDesktopId(nint topLevelWindow, out Guid desktopId);

    [PreserveSig]
    int MoveWindowToDesktop(nint topLevelWindow, ref Guid desktopId);
}

internal sealed class Win32SessionGuard : ISessionGuard
{
    public SessionDenial Evaluate()
    {
        if (!IsDefaultInputDesktop())
            return SessionDenial.SecureDesktop;
        if (!IsSessionInteractive())
            return SessionDenial.NotInteractive;
        return SessionDenial.None;
    }

    private static bool IsDefaultInputDesktop()
    {
        var input = NativeMethods.OpenInputDesktop(0, false, NativeMethods.DESKTOP_READOBJECTS);
        if (input == 0)
            return false;
        try
        {
            var sb = new System.Text.StringBuilder(256);
            if (!NativeMethods.GetUserObjectInformation(input, NativeMethods.UOI_NAME, sb, sb.Capacity * 2, out _))
                return false;
            return string.Equals(sb.ToString(), "Default", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            NativeMethods.CloseDesktop(input);
        }
    }

    private static bool IsSessionInteractive()
    {
        if (!NativeMethods.ProcessIdToSessionId(NativeMethods.GetCurrentProcessId(), out var sessionId))
            return false;
        if (!NativeMethods.WTSQuerySessionInformation(0, sessionId, NativeMethods.WTSConnectState, out var buffer, out _))
            return false;
        try
        {
            var state = Marshal.ReadInt32(buffer);
            return state is NativeMethods.WTSActive or NativeMethods.WTSConnected;
        }
        finally
        {
            NativeMethods.WTSFreeMemory(buffer);
        }
    }
}

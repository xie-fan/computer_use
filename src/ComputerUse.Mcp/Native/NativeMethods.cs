using System.Runtime.InteropServices;
using System.Text;

namespace ComputerUse.Mcp.Native;

internal static partial class NativeMethods
{
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const int GW_OWNER = 4;
    public const int GA_ROOT = 2;
    public const int GA_ROOTOWNER = 3;

    public const int WS_VISIBLE = 0x10000000;
    public const int WS_MINIMIZE = 0x20000000;
    public const int WS_EX_TOOLWINDOW = 0x00000080;

    public const uint WM_PRINT = 0x0317;
    public const uint PW_CLIENTONLY = 0x00000001;
    public const uint PW_RENDERFULLCONTENT = 0x00000002;

    public const int SW_RESTORE = 9;
    public const int SW_SHOW = 5;

    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const uint MONITORINFOF_PRIMARY = 1;

    public const int SM_XVIRTUALSCREEN = 76;
    public const int SM_YVIRTUALSCREEN = 77;
    public const int SM_CXVIRTUALSCREEN = 78;
    public const int SM_CYVIRTUALSCREEN = 79;
    public const int SM_SWAPBUTTON = 23;
    public const int SM_CXDOUBLECLK = 36;
    public const int SM_CYDOUBLECLK = 37;

    public const uint MDT_EFFECTIVE_DPI = 0;

    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const int DWMWA_CLOAKED = 14;

    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint PROCESS_QUERY_INFORMATION = 0x0400;
    public const uint TOKEN_QUERY = 0x0008;
    public const int TokenIntegrityLevel = 25;

    public const uint DESKTOP_READOBJECTS = 0x0001;
    public const int UOI_NAME = 2;

    public const int WTSConnectState = 8;
    public const int WTSActive = 0;
    public const int WTSConnected = 1;

    public const uint INPUT_MOUSE = 0;
    public const uint INPUT_KEYBOARD = 1;
    public const uint MOUSEEVENTF_MOVE = 0x0001;
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;
    public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    public const uint MOUSEEVENTF_WHEEL = 0x0800;
    public const uint MOUSEEVENTF_HWHEEL = 0x1000;
    public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    public const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004;
    public const int WHEEL_DELTA = 120;
    public const ushort VK_CONTROL = 0x11;
    public const ushort VK_V = 0x56;

    public const uint CF_UNICODETEXT = 13;
    public const uint GMEM_MOVEABLE = 0x0002;
    public const uint BI_RGB = 0;
    public const uint DIB_RGB_COLORS = 0;
    public const int SRCCOPY = 0x00CC0020;

    public const int D3D11_SDK_VERSION = 7;
    public const int D3D_DRIVER_TYPE_HARDWARE = 1;
    public const int D3D_DRIVER_TYPE_WARP = 5;
    public const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
    public const uint D3D11_USAGE_STAGING = 3;
    public const uint D3D11_CPU_ACCESS_READ = 0x20000;
    public const uint DXGI_FORMAT_B8G8R8A8_UNORM = 87;
    public const uint D3D11_MAP_READ = 1;

    public static readonly Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    public static readonly Guid IID_ID3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    public static readonly Guid IID_IGraphicsCaptureItem = new("79c3f95b-31f7-4ec2-a464-632ef5d30760");
    public static readonly Guid CLSID_VirtualDesktopManager = new("aa509086-5ca9-4c25-8f95-589d3c07b48a");
    public static readonly Guid IID_IVirtualDesktopManager = new("a5cd92ff-29be-454c-8d04-d82879fb3f1b");

    public static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = (nint)(-4);

    public delegate bool EnumWindowsProc(nint hWnd, nint lParam);
    public delegate bool EnumMonitorsProc(nint hMonitor, nint hdc, nint lprcMonitor, nint dwData);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip, EnumMonitorsProc lpfnEnum, nint dwData);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextLength(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern nint GetWindow(nint hWnd, uint uCmd);

    [DllImport("user32.dll")]
    public static extern nint GetAncestor(nint hWnd, uint gaFlags);

    [DllImport("user32.dll")]
    public static extern nint GetParent(nint hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool BringWindowToTop(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    public static extern bool AllowSetForegroundWindow(uint dwProcessId);

    [DllImport("user32.dll")]
    public static extern nint WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern nint MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    public static extern uint GetDoubleClickTime();

    [DllImport("user32.dll")]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(nint hwnd, nint hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    public static extern nint GetWindowDC(nint hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(nint hWnd, nint hDC);

    [DllImport("user32.dll")]
    public static extern nint OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll")]
    public static extern bool CloseDesktop(nint hDesktop);

    [DllImport("user32.dll")]
    public static extern nint GetThreadDesktop(uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetUserObjectInformation(nint hObj, int nIndex, StringBuilder pvInfo, int nLength, out int lpnLengthNeeded);

    [DllImport("user32.dll")]
    public static extern bool SetProcessDpiAwarenessContext(nint value);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll")]
    public static extern bool OpenClipboard(nint hWndNewOwner);

    [DllImport("user32.dll")]
    public static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    public static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    public static extern nint SetClipboardData(uint uFormat, nint hMem);

    [DllImport("user32.dll")]
    public static extern nint GetClipboardData(uint uFormat);

    [DllImport("user32.dll")]
    public static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    public static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll")]
    public static extern bool PeekMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern nint DispatchMessage(ref MSG lpMsg);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentProcessId();

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(nint hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetProcessTimes(nint hProcess, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool QueryFullProcessImageName(nint hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool Process32First(nint hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool Process32Next(nint hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll")]
    public static extern nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [DllImport("kernel32.dll")]
    public static extern nint GlobalLock(nint hMem);

    [DllImport("kernel32.dll")]
    public static extern nint GlobalFree(nint hMem);

    [DllImport("kernel32.dll")]
    public static extern bool GlobalUnlock(nint hMem);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool GetTokenInformation(nint tokenHandle, int tokenInformationClass, nint tokenInformation, int tokenInformationLength, out int returnLength);

    [DllImport("advapi32.dll")]
    public static extern nint GetSidSubAuthority(nint pSid, uint nSubAuthority);

    [DllImport("advapi32.dll")]
    public static extern nint GetSidSubAuthorityCount(nint pSid);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(nint hmonitor, uint dpiType, out uint dpiX, out uint dpiY);

    [DllImport("wtsapi32.dll")]
    public static extern bool WTSQuerySessionInformation(nint hServer, uint sessionId, int wtsInfoClass, out nint ppBuffer, out uint pBytesReturned);

    [DllImport("wtsapi32.dll")]
    public static extern void WTSFreeMemory(nint memory);

    [DllImport("kernel32.dll")]
    public static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ProcessIdToSessionId(uint dwProcessId, out uint pSessionId);

    [DllImport("ole32.dll")]
    public static extern int OleInitialize(nint pvReserved);

    [DllImport("ole32.dll")]
    public static extern void OleUninitialize();

    [DllImport("ole32.dll")]
    public static extern int CoCreateInstance(ref Guid rclsid, nint pUnkOuter, uint dwClsContext, ref Guid riid, out nint ppv);

    [DllImport("gdi32.dll")]
    public static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    public static extern nint CreateCompatibleBitmap(nint hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    public static extern nint CreateDIBSection(nint hdc, ref BITMAPINFO pbmi, uint usage, out nint ppvBits, nint hSection, uint offset);

    [DllImport("gdi32.dll")]
    public static extern nint SelectObject(nint hdc, nint h);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(nint ho);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(nint hdc);

    [DllImport("d3d11.dll")]
    public static extern int D3D11CreateDevice(
        nint pAdapter,
        int driverType,
        nint software,
        uint flags,
        nint pFeatureLevels,
        uint featureLevels,
        uint sdkVersion,
        out nint ppDevice,
        out int pFeatureLevel,
        out nint ppImmediateContext);

    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice")]
    public static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public nint th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_TEXTURE2D_DESC
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public uint Format;
        public uint SampleCount;
        public uint SampleQuality;
        public uint Usage;
        public uint BindFlags;
        public uint CPUAccessFlags;
        public uint MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_MAPPED_SUBRESOURCE
    {
        public nint pData;
        public uint RowPitch;
        public uint DepthPitch;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TOKEN_MANDATORY_LABEL
    {
        public nint Sid;
        public uint Attributes;
    }

    public const uint TH32CS_SNAPPROCESS = 0x00000002;
    public const uint CLSCTX_INPROC_SERVER = 1;
    public const uint PM_REMOVE = 0x0001;
}

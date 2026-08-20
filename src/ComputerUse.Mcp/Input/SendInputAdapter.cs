using System.Runtime.InteropServices;
using ComputerUse.Mcp.Abstractions;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Native;

namespace ComputerUse.Mcp.Input;

internal sealed class SendInputAdapter : IInputInjector
{
    public bool SwapMouseButtons => NativeMethods.GetSystemMetrics(NativeMethods.SM_SWAPBUTTON) != 0;
    public int DoubleClickTimeMs => (int)Math.Max(1, NativeMethods.GetDoubleClickTime());

    public ScreenPoint GetCursorPos()
    {
        NativeMethods.GetCursorPos(out var p);
        return new ScreenPoint(p.X, p.Y);
    }

    public void MoveAbsoluteVirtualDesk(int physicalX, int physicalY)
    {
        var vx = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        var vy = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        var vw = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        var vh = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        var nx = Normalize(physicalX, vx, vw);
        var ny = Normalize(physicalY, vy, vh);
        Send(new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            U = new NativeMethods.InputUnion
            {
                mi = new NativeMethods.MOUSEINPUT
                {
                    dx = nx,
                    dy = ny,
                    dwFlags = NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE | NativeMethods.MOUSEEVENTF_VIRTUALDESK
                }
            }
        });
    }

    public void MouseButton(MouseButtonKind logicalButton, bool down)
    {
        var physical = ToPhysical(logicalButton);
        uint flags = (physical, down) switch
        {
            (MouseButtonKind.Left, true) => NativeMethods.MOUSEEVENTF_LEFTDOWN,
            (MouseButtonKind.Left, false) => NativeMethods.MOUSEEVENTF_LEFTUP,
            (MouseButtonKind.Right, true) => NativeMethods.MOUSEEVENTF_RIGHTDOWN,
            (MouseButtonKind.Right, false) => NativeMethods.MOUSEEVENTF_RIGHTUP,
            (MouseButtonKind.Middle, true) => NativeMethods.MOUSEEVENTF_MIDDLEDOWN,
            (MouseButtonKind.Middle, false) => NativeMethods.MOUSEEVENTF_MIDDLEUP,
            _ => 0
        };
        Send(new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            U = new NativeMethods.InputUnion { mi = new NativeMethods.MOUSEINPUT { dwFlags = flags } }
        });
    }

    public void Scroll(int dxNotches, int dyNotches)
    {
        if (dyNotches != 0)
        {
            var data = unchecked((uint)(-dyNotches * NativeMethods.WHEEL_DELTA));
            Send(new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_MOUSE,
                U = new NativeMethods.InputUnion
                {
                    mi = new NativeMethods.MOUSEINPUT
                    {
                        mouseData = data,
                        dwFlags = NativeMethods.MOUSEEVENTF_WHEEL
                    }
                }
            });
        }

        if (dxNotches != 0)
        {
            var data = unchecked((uint)(dxNotches * NativeMethods.WHEEL_DELTA));
            Send(new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_MOUSE,
                U = new NativeMethods.InputUnion
                {
                    mi = new NativeMethods.MOUSEINPUT
                    {
                        mouseData = data,
                        dwFlags = NativeMethods.MOUSEEVENTF_HWHEEL
                    }
                }
            });
        }
    }

    public void Key(ushort virtualKey, bool down, bool extended)
    {
        uint flags = 0;
        if (!down)
            flags |= NativeMethods.KEYEVENTF_KEYUP;
        if (extended)
            flags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;
        Send(new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = virtualKey,
                    dwFlags = flags
                }
            }
        });
    }

    public void Unicode(char codeUnit, bool down)
    {
        uint flags = NativeMethods.KEYEVENTF_UNICODE;
        if (!down)
            flags |= NativeMethods.KEYEVENTF_KEYUP;
        Send(new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wScan = codeUnit,
                    dwFlags = flags
                }
            }
        });
    }

    private MouseButtonKind ToPhysical(MouseButtonKind logical)
    {
        if (!SwapMouseButtons)
            return logical;
        return logical switch
        {
            MouseButtonKind.Left => MouseButtonKind.Right,
            MouseButtonKind.Right => MouseButtonKind.Left,
            _ => logical
        };
    }

    private static int Normalize(int physical, int origin, int size)
    {
        if (size <= 1)
            return 0;
        var n = (int)Math.Round((physical - origin) * 65535.0 / (size - 1));
        if (n < 0)
            return 0;
        if (n > 65535)
            return 65535;
        return n;
    }

    private static void Send(NativeMethods.INPUT input)
    {
        var sent = NativeMethods.SendInput(1, [input], Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent != 1)
            throw new ComputerUseException(ErrorCodes.ActionFailed, "SendInput was rejected by the OS.");
    }
}

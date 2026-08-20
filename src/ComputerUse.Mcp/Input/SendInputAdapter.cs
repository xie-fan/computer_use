using ComputerUse.Mcp.Abstractions;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Native;

namespace ComputerUse.Mcp.Input;

internal sealed class SendInputAdapter : IInputInjector
{
    internal delegate uint SendInputsFn(ReadOnlySpan<NativeMethods.INPUT> inputs);

    private const int UnicodeBatchEvents = 64;
    private readonly SendInputsFn _send;

    private int _virtualX;
    private int _virtualY;
    private int _virtualW;
    private int _virtualH;
    private bool _swapButtons;
    private int _doubleClickMs = 500;
    private bool _metricsLoaded;

    public SendInputAdapter() : this(NativeMethods.SendInputs)
    {
    }

    internal SendInputAdapter(SendInputsFn send)
    {
        _send = send;
    }

    public bool SwapMouseButtons
    {
        get
        {
            EnsureMetrics();
            return _swapButtons;
        }
    }

    public int DoubleClickTimeMs
    {
        get
        {
            EnsureMetrics();
            return _doubleClickMs;
        }
    }

    public void RefreshMetrics()
    {
        _virtualX = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        _virtualY = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        _virtualW = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        _virtualH = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        _swapButtons = NativeMethods.GetSystemMetrics(NativeMethods.SM_SWAPBUTTON) != 0;
        _doubleClickMs = (int)Math.Max(1, NativeMethods.GetDoubleClickTime());
        _metricsLoaded = true;
    }

    public ScreenPoint GetCursorPos()
    {
        NativeMethods.GetCursorPos(out var p);
        return new ScreenPoint(p.X, p.Y);
    }

    public void MoveAbsoluteVirtualDesk(int physicalX, int physicalY)
    {
        EnsureMetrics();
        var nx = Normalize(physicalX, _virtualX, _virtualW);
        var ny = Normalize(physicalY, _virtualY, _virtualH);
        Send(stackalloc NativeMethods.INPUT[]
        {
            MouseMove(nx, ny)
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
        Send(stackalloc NativeMethods.INPUT[] { Mouse(flags) });
    }

    public void Scroll(int dxNotches, int dyNotches)
    {
        var count = (dyNotches != 0 ? 1 : 0) + (dxNotches != 0 ? 1 : 0);
        if (count == 0)
            return;

        Span<NativeMethods.INPUT> events = stackalloc NativeMethods.INPUT[count];
        var n = 0;
        if (dyNotches != 0)
        {
            var data = unchecked((uint)(-dyNotches * NativeMethods.WHEEL_DELTA));
            events[n++] = Mouse(NativeMethods.MOUSEEVENTF_WHEEL, data);
        }

        if (dxNotches != 0)
        {
            var data = unchecked((uint)(dxNotches * NativeMethods.WHEEL_DELTA));
            events[n++] = Mouse(NativeMethods.MOUSEEVENTF_HWHEEL, data);
        }

        Send(events[..n]);
    }

    public void Key(ushort virtualKey, bool down, bool extended)
    {
        Send(stackalloc NativeMethods.INPUT[] { Keyboard(virtualKey, down, extended) });
    }

    public void KeyStroke(ushort virtualKey, bool extended, bool ctrl, bool alt, bool shift)
    {
        Span<NativeMethods.INPUT> events = stackalloc NativeMethods.INPUT[8];
        var n = 0;
        if (ctrl)
            events[n++] = Keyboard(NativeMethods.VK_CONTROL, true, false);
        if (alt)
            events[n++] = Keyboard(NativeMethods.VK_MENU, true, false);
        if (shift)
            events[n++] = Keyboard(NativeMethods.VK_SHIFT, true, false);
        events[n++] = Keyboard(virtualKey, true, extended);
        events[n++] = Keyboard(virtualKey, false, extended);
        if (shift)
            events[n++] = Keyboard(NativeMethods.VK_SHIFT, false, false);
        if (alt)
            events[n++] = Keyboard(NativeMethods.VK_MENU, false, false);
        if (ctrl)
            events[n++] = Keyboard(NativeMethods.VK_CONTROL, false, false);
        Send(events[..n]);
    }

    public void Unicode(char codeUnit, bool down)
    {
        Send(stackalloc NativeMethods.INPUT[] { UnicodeKey(codeUnit, down) });
    }

    public void UnicodeText(ReadOnlySpan<char> codeUnits)
    {
        Span<NativeMethods.INPUT> buffer = stackalloc NativeMethods.INPUT[UnicodeBatchEvents];
        var n = 0;
        foreach (var ch in codeUnits)
        {
            if (n + 2 > buffer.Length)
            {
                Send(buffer[..n]);
                n = 0;
            }

            buffer[n++] = UnicodeKey(ch, true);
            buffer[n++] = UnicodeKey(ch, false);
        }

        if (n > 0)
            Send(buffer[..n]);
    }

    private void EnsureMetrics()
    {
        if (!_metricsLoaded)
            RefreshMetrics();
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

    private void Send(ReadOnlySpan<NativeMethods.INPUT> inputs)
    {
        if (inputs.IsEmpty)
            return;
        var sent = _send(inputs);
        if (sent == (uint)inputs.Length)
            return;

        if ((sent & 1) == 1)
            TryReleaseLastKeyboardDown(inputs[(int)sent - 1]);

        throw new ComputerUseException(ErrorCodes.ActionFailed, "SendInput was rejected by the OS.");
    }

    private void TryReleaseLastKeyboardDown(NativeMethods.INPUT last)
    {
        if (last.type != NativeMethods.INPUT_KEYBOARD)
            return;
        if ((last.U.ki.dwFlags & NativeMethods.KEYEVENTF_KEYUP) != 0)
            return;
        try
        {
            var up = last;
            up.U.ki.dwFlags |= NativeMethods.KEYEVENTF_KEYUP;
            _ = _send([up]);
        }
        catch
        {
            // best-effort; operate finally may still ReleaseAll
        }
    }

    private static NativeMethods.INPUT MouseMove(int nx, int ny) => new()
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
    };

    private static NativeMethods.INPUT Mouse(uint flags, uint mouseData = 0) => new()
    {
        type = NativeMethods.INPUT_MOUSE,
        U = new NativeMethods.InputUnion
        {
            mi = new NativeMethods.MOUSEINPUT
            {
                mouseData = mouseData,
                dwFlags = flags
            }
        }
    };

    private static NativeMethods.INPUT Keyboard(ushort virtualKey, bool down, bool extended)
    {
        uint flags = 0;
        if (!down)
            flags |= NativeMethods.KEYEVENTF_KEYUP;
        if (extended)
            flags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;
        return new NativeMethods.INPUT
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
        };
    }

    private static NativeMethods.INPUT UnicodeKey(char codeUnit, bool down)
    {
        uint flags = NativeMethods.KEYEVENTF_UNICODE;
        if (!down)
            flags |= NativeMethods.KEYEVENTF_KEYUP;
        return new NativeMethods.INPUT
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
        };
    }
}

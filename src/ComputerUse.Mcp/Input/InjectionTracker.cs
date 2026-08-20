using ComputerUse.Mcp.Abstractions;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Native;

namespace ComputerUse.Mcp.Input;

internal sealed class InjectionTracker(IInputInjector input)
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

    public void KeyStroke(ushort virtualKey, bool extended, bool ctrl, bool alt, bool shift)
    {
        try
        {
            input.KeyStroke(virtualKey, extended, ctrl, alt, shift);
        }
        catch
        {
            try { input.Key(virtualKey, false, extended); } catch { /* best-effort */ }
            if (shift)
                try { input.Key(NativeMethods.VK_SHIFT, false, false); } catch { /* best-effort */ }
            if (alt)
                try { input.Key(NativeMethods.VK_MENU, false, false); } catch { /* best-effort */ }
            if (ctrl)
                try { input.Key(NativeMethods.VK_CONTROL, false, false); } catch { /* best-effort */ }
            throw;
        }
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

    public void UnicodeText(ReadOnlySpan<char> codeUnits)
    {
        if (codeUnits.IsEmpty)
            return;
        input.UnicodeText(codeUnits);
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

    internal int DownCount => _down.Count;

    private void RemoveLast(Pressed match)
    {
        if (_down.Count == 0)
            return;
        if (_down.Peek() == match)
        {
            _down.Pop();
            return;
        }

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

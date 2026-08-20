using System.Runtime.InteropServices;
using ComputerUse.Mcp.Abstractions;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Native;

namespace ComputerUse.Mcp.Input;

internal sealed class ClipboardWorker : IClipboardWorker
{
    private readonly NativeStaDispatcher _sta;
    private readonly IInputInjector _input;

    public ClipboardWorker(NativeStaDispatcher sta, IInputInjector input)
    {
        _sta = sta;
        _input = input;
    }

    public Task<ClipboardPasteResult> PasteUnicodeAsync(
        string value,
        Func<bool> confirmForeground,
        int restoreWaitMs,
        CancellationToken cancellationToken)
    {
        return _sta.InvokeAsync(() => PasteOnSta(value, confirmForeground, restoreWaitMs, _input), cancellationToken);
    }

    private static ClipboardPasteResult PasteOnSta(
        string value,
        Func<bool> confirmForeground,
        int restoreWaitMs,
        IInputInjector input)
    {
        string? previous = null;
        uint afterWrite;
        try
        {
            previous = TryReadUnicode();
            WriteUnicode(value);
            afterWrite = NativeMethods.GetClipboardSequenceNumber();
        }
        catch (Exception)
        {
            throw new ComputerUseException(ErrorCodes.ClipboardFailed, "The clipboard could not be opened or written.");
        }

        if (!confirmForeground())
            throw new ComputerUseException(ErrorCodes.FocusLost, "Foreground window changed before paste.");

        SendCtrlV(input);

        if (!ClipboardSequenceWait.StillUnchanged(
                afterWrite,
                restoreWaitMs,
                NativeMethods.GetClipboardSequenceNumber,
                Thread.Sleep))
        {
            return new ClipboardPasteResult(false, false, "clipboard_not_restored", "Clipboard changed before restore.");
        }

        try
        {
            if (previous is null)
                ClearClipboard();
            else
                WriteUnicode(previous);
            return new ClipboardPasteResult(true, false, null, null);
        }
        catch
        {
            return new ClipboardPasteResult(false, false, "clipboard_not_restored", "Clipboard restore failed.");
        }
    }

    private static string? TryReadUnicode()
    {
        if (!NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_UNICODETEXT))
            return null;
        if (!NativeMethods.OpenClipboard(0))
            return null;
        try
        {
            var handle = NativeMethods.GetClipboardData(NativeMethods.CF_UNICODETEXT);
            if (handle == 0)
                return null;
            var ptr = NativeMethods.GlobalLock(handle);
            if (ptr == 0)
                return null;
            try
            {
                return Marshal.PtrToStringUni(ptr);
            }
            finally
            {
                NativeMethods.GlobalUnlock(handle);
            }
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    private static void WriteUnicode(string value)
    {
        var bytes = (value.Length + 1) * 2;
        var hGlobal = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, (nuint)bytes);
        if (hGlobal == 0)
            throw new ComputerUseException(ErrorCodes.ClipboardFailed, "GlobalAlloc failed.");
        var ptr = NativeMethods.GlobalLock(hGlobal);
        if (ptr == 0)
            throw new ComputerUseException(ErrorCodes.ClipboardFailed, "GlobalLock failed.");
        try
        {
            unsafe
            {
                var dest = new Span<char>((char*)ptr, value.Length + 1);
                value.AsSpan().CopyTo(dest);
                dest[value.Length] = '\0';
            }
        }
        finally
        {
            NativeMethods.GlobalUnlock(hGlobal);
        }

        if (!NativeMethods.OpenClipboard(0))
            throw new ComputerUseException(ErrorCodes.ClipboardFailed, "OpenClipboard failed.");
        try
        {
            NativeMethods.EmptyClipboard();
            if (NativeMethods.SetClipboardData(NativeMethods.CF_UNICODETEXT, hGlobal) == 0)
                throw new ComputerUseException(ErrorCodes.ClipboardFailed, "SetClipboardData failed.");
            hGlobal = 0;
        }
        finally
        {
            NativeMethods.CloseClipboard();
            if (hGlobal != 0)
                NativeMethods.GlobalFree(hGlobal);
        }
    }

    private static void ClearClipboard()
    {
        if (!NativeMethods.OpenClipboard(0))
            return;
        try { NativeMethods.EmptyClipboard(); }
        finally { NativeMethods.CloseClipboard(); }
    }

    private static void SendCtrlV(IInputInjector input)
    {
        try
        {
            input.KeyStroke(NativeMethods.VK_V, false, true, false, false);
        }
        catch
        {
            try { input.Key(NativeMethods.VK_V, false, false); } catch { /* best-effort */ }
            try { input.Key(NativeMethods.VK_CONTROL, false, false); } catch { /* best-effort */ }
            throw;
        }
    }
}

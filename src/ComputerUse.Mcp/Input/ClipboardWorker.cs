using System.Runtime.InteropServices;
using ComputerUse.Mcp.Abstractions;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Native;

namespace ComputerUse.Mcp.Input;

internal sealed class ClipboardWorker : IClipboardWorker
{
    private readonly NativeStaDispatcher _sta;

    public ClipboardWorker(NativeStaDispatcher sta)
    {
        _sta = sta;
    }

    public Task<ClipboardPasteResult> PasteUnicodeAsync(
        string value,
        Func<bool> confirmForeground,
        int restoreWaitMs,
        CancellationToken cancellationToken)
    {
        return _sta.InvokeAsync(() => PasteOnSta(value, confirmForeground, restoreWaitMs), cancellationToken);
    }

    private static ClipboardPasteResult PasteOnSta(string value, Func<bool> confirmForeground, int restoreWaitMs)
    {
        string? previous = null;
        uint afterWrite;
        try
        {
            _ = NativeMethods.GetClipboardSequenceNumber();
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

        SendCtrlV();

        var waitUntil = Environment.TickCount64 + Math.Max(0, restoreWaitMs);
        while (Environment.TickCount64 < waitUntil)
            Thread.Sleep(20);

        var current = NativeMethods.GetClipboardSequenceNumber();
        if (current != afterWrite)
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
            Marshal.Copy(value.ToCharArray(), 0, ptr, value.Length);
            Marshal.WriteInt16(ptr, value.Length * 2, 0);
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

    private static void SendCtrlV()
    {
        var adapter = new SendInputAdapter();
        var ctrl = false;
        var v = false;
        try
        {
            adapter.Key(NativeMethods.VK_CONTROL, true, false);
            ctrl = true;
            adapter.Key(NativeMethods.VK_V, true, false);
            v = true;
            adapter.Key(NativeMethods.VK_V, false, false);
            v = false;
            adapter.Key(NativeMethods.VK_CONTROL, false, false);
            ctrl = false;
        }
        finally
        {
            try { if (v) adapter.Key(NativeMethods.VK_V, false, false); } catch { /* best-effort */ }
            try { if (ctrl) adapter.Key(NativeMethods.VK_CONTROL, false, false); } catch { /* best-effort */ }
        }
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Native;

namespace ComputerUse.Mcp.Capture;

internal static class PrintWindowHelper
{
    public const string Argument = "--print-window-helper";

    public static int Run(string[] args)
    {
        NativeMethods.SetProcessDpiAwarenessContext(NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        if (args.Length < 2 || !ulong.TryParse(args[1], out var hwndValue))
        {
            Console.Error.WriteLine("print-window-helper: invalid arguments");
            return 1;
        }

        var hwnd = (nint)hwndValue;
        CapturedBitmap? bmp = null;
        try
        {
            bmp = Capture(hwnd);
            using var stdout = Console.OpenStandardOutput();
            PrintWindowBgraCodec.Write(stdout, bmp);
            return 0;
        }
        catch (ComputerUseException ex)
        {
            Console.Error.WriteLine(ex.Code + ": " + ex.Message);
            return ex.Code switch
            {
                ErrorCodes.EmptyFrame => 2,
                ErrorCodes.CaptureUnsupported => 3,
                _ => 4
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("print-window-helper failed");
            Console.Error.WriteLine(ex.GetType().FullName);
            return 4;
        }
        finally
        {
            bmp?.Return();
        }
    }

    public static CapturedBitmap Capture(nint hwnd)
    {
        if (!NativeMethods.IsWindow(hwnd) || !NativeMethods.GetWindowRect(hwnd, out var rect))
            throw new ComputerUseException(ErrorCodes.CaptureFailed, "PrintWindow could not read the window rectangle.");
        var width = Math.Max(1, rect.Width);
        var height = Math.Max(1, rect.Height);

        var bmi = new NativeMethods.BITMAPINFO
        {
            bmiHeader = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = NativeMethods.BI_RGB
            }
        };

        var screenDc = NativeMethods.GetWindowDC(hwnd);
        if (screenDc == 0)
            throw new ComputerUseException(ErrorCodes.CaptureFailed, "PrintWindow could not get a window DC.");
        nint memDc = 0;
        nint dib = 0;
        nint old = 0;
        try
        {
            memDc = NativeMethods.CreateCompatibleDC(screenDc);
            dib = NativeMethods.CreateDIBSection(memDc, ref bmi, NativeMethods.DIB_RGB_COLORS, out var bits, 0, 0);
            if (memDc == 0 || dib == 0 || bits == 0)
                throw new ComputerUseException(ErrorCodes.CaptureFailed, "PrintWindow could not allocate a DIB.");
            old = NativeMethods.SelectObject(memDc, dib);
            var ok = NativeMethods.PrintWindow(hwnd, memDc, NativeMethods.PW_RENDERFULLCONTENT);
            if (!ok)
                ok = NativeMethods.PrintWindow(hwnd, memDc, 0);
            if (!ok)
                throw new ComputerUseException(ErrorCodes.CaptureUnsupported, "PrintWindow is not supported for this window.");

            var stride = width * 4;
            CapturedBitmap? captured = CapturedBitmap.Rent(width, height, stride, "print_window");
            try
            {
                Marshal.Copy(bits, captured.Bgra, 0, captured.ByteLength);
                if (BgraEmptyFrame.IsEmpty(captured.Bgra, width, height, stride))
                    throw new ComputerUseException(ErrorCodes.EmptyFrame, "PrintWindow returned an empty frame.");
                var result = captured;
                captured = null;
                return result;
            }
            finally
            {
                captured?.Return();
            }
        }
        finally
        {
            if (old != 0)
                NativeMethods.SelectObject(memDc, old);
            if (dib != 0)
                NativeMethods.DeleteObject(dib);
            if (memDc != 0)
                NativeMethods.DeleteDC(memDc);
            NativeMethods.ReleaseDC(hwnd, screenDc);
        }
    }
}

internal sealed class PrintWindowProcessCapture
{
    public CapturedBitmap Capture(nint hwnd, int timeoutMs)
    {
        var (file, args) = HelperStartInfo(hwnd);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(file)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        foreach (var a in args)
            process.StartInfo.ArgumentList.Add(a);

        if (!process.Start())
            throw new ComputerUseException(ErrorCodes.CaptureFailed, "Failed to start the PrintWindow helper.");

        var stdout = process.StandardOutput.BaseStream;
        var readTask = Task.Run(() => PrintWindowBgraCodec.Read(stdout));

        if (!process.WaitForExit(timeoutMs))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            Observe(readTask);
            throw new ComputerUseException(ErrorCodes.CaptureTimeout, "PrintWindow helper timed out.");
        }

        if (process.ExitCode != 0)
        {
            Observe(readTask);
            throw process.ExitCode switch
            {
                2 => new ComputerUseException(ErrorCodes.EmptyFrame, "PrintWindow returned an empty frame."),
                3 => new ComputerUseException(ErrorCodes.CaptureUnsupported, "PrintWindow is not supported for this window."),
                _ => new ComputerUseException(ErrorCodes.CaptureFailed, "PrintWindow helper failed.")
            };
        }

        try
        {
            return readTask.GetAwaiter().GetResult();
        }
        catch (ComputerUseException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new ComputerUseException(ErrorCodes.CaptureFailed, "PrintWindow helper failed.");
        }
    }

    private static void Observe(Task task)
    {
        try { task.Wait(TimeSpan.FromMilliseconds(100)); }
        catch { /* drain so a failed Read is not unobserved */ }
    }

    private static (string File, string[] Args) HelperStartInfo(nint hwnd)
    {
        var hwndText = unchecked((ulong)hwnd).ToString();
        var entry = Environment.ProcessPath;
        var dll = typeof(PrintWindowHelper).Assembly.Location;
        if (!string.IsNullOrEmpty(entry)
            && Path.GetFileNameWithoutExtension(entry).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(dll))
        {
            return (entry, ["exec", dll, PrintWindowHelper.Argument, hwndText]);
        }

        if (!string.IsNullOrEmpty(entry))
            return (entry, [PrintWindowHelper.Argument, hwndText]);
        if (!string.IsNullOrEmpty(dll))
            return ("dotnet", ["exec", dll, PrintWindowHelper.Argument, hwndText]);
        throw new ComputerUseException(ErrorCodes.CaptureFailed, "Cannot locate this executable for PrintWindow isolation.");
    }
}

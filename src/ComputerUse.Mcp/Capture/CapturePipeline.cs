using ComputerUse.Mcp.Abstractions;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Native;

namespace ComputerUse.Mcp.Capture;

internal sealed class CapturePipeline : ICapturePipeline, IDisposable
{
    private readonly NativeStaDispatcher _sta;
    private readonly WgcCaptureAdapter _wgc = new();
    private readonly PrintWindowProcessCapture _printWindow = new();

    public CapturePipeline(NativeStaDispatcher sta)
    {
        _sta = sta;
    }

    public async Task<CapturedBitmap> CaptureAsync(nint hwnd, int timeoutMs, CancellationToken cancellationToken)
    {
        ComputerUseException? wgcError = null;
        try
        {
            return await _sta.InvokeAsync(() => _wgc.Capture(hwnd, timeoutMs), cancellationToken).ConfigureAwait(false);
        }
        catch (ComputerUseException ex) when (
            ex.Code is ErrorCodes.CaptureFailed
                or ErrorCodes.CaptureTimeout
                or ErrorCodes.CaptureUnsupported
                or ErrorCodes.EmptyFrame
                or ErrorCodes.ProtectedContent)
        {
            wgcError = ex;
        }

        try
        {
            return await Task.Run(() => _printWindow.Capture(hwnd, timeoutMs), cancellationToken).ConfigureAwait(false);
        }
        catch (ComputerUseException)
        {
            throw wgcError ?? new ComputerUseException(ErrorCodes.CaptureFailed, "Capture failed.");
        }
    }

    public void Dispose()
    {
        try
        {
            _sta.InvokeAsync(() => _wgc.Dispose(), CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
            // Host shutdown may have already torn down the STA dispatcher.
        }
    }
}

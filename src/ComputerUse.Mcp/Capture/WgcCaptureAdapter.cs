using System.Runtime.InteropServices;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Native;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace ComputerUse.Mcp.Capture;

internal sealed class WgcCaptureAdapter : IDisposable
{
    private const int MaxCachedSessions = 2;
    private static readonly TimeSpan SessionTtl = TimeSpan.FromSeconds(3);

    private readonly TtlLruCache<nint, CachedSession> _sessions = new(MaxCachedSessions, SessionTtl);

    private nint _d3dDevice;
    private nint _d3dContext;
    private IDirect3DDevice? _winrtDevice;
    private nint _staging;
    private uint _stagingWidth;
    private uint _stagingHeight;
    private uint _stagingFormat;

    private CreateTexture2DDelegate? _createTexture2D;
    private CopySubresourceRegionDelegate? _copySubresourceRegion;
    private MapDelegate? _map;
    private UnmapDelegate? _unmap;
    private GetDescDelegate? _getDesc;

    public CapturedBitmap Capture(nint hwnd, int timeoutMs)
    {
        if (!NativeMethods.IsWindow(hwnd))
            throw new ComputerUseException(ErrorCodes.CaptureFailed, "Window is gone.");

        try
        {
            return CaptureOnce(hwnd, timeoutMs, allowDeviceReset: true);
        }
        catch (Exception ex) when (IsAccessDenied(ex))
        {
            throw new ComputerUseException(ErrorCodes.ProtectedContent, "The window content is protected.");
        }
        catch (ComputerUseException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new ComputerUseException(ErrorCodes.CaptureUnsupported, "Windows Graphics Capture cannot capture this window.");
        }
    }

    public void Dispose()
    {
        foreach (var session in _sessions.Drain())
            CloseSession(session);
        ReleaseStaging();
        ReleaseDevice();
    }

    private CapturedBitmap CaptureOnce(nint hwnd, int timeoutMs, bool allowDeviceReset)
    {
        try
        {
            EnsureDevice();
            foreach (var expired in _sessions.EvictExpired())
                CloseSession(expired);

            var cached = _sessions.TryGet(hwnd, out var session);
            if (!cached)
            {
                session = OpenSession(hwnd);
                foreach (var evicted in _sessions.Put(hwnd, session))
                    CloseSession(evicted);
            }

            try
            {
                return CaptureFromSession(session!, timeoutMs);
            }
            catch (ComputerUseException ex) when (cached && ex.Code == ErrorCodes.CaptureTimeout)
            {
                DropSession(hwnd);
                var retryMs = Math.Max(1, timeoutMs / 2);
                session = OpenSession(hwnd);
                foreach (var evicted in _sessions.Put(hwnd, session))
                    CloseSession(evicted);
                return CaptureFromSession(session, retryMs);
            }
            catch (ComputerUseException)
            {
                DropSession(hwnd);
                throw;
            }
            catch (Exception) when (cached)
            {
                DropSession(hwnd);
                session = OpenSession(hwnd);
                foreach (var evicted in _sessions.Put(hwnd, session))
                    CloseSession(evicted);
                return CaptureFromSession(session, timeoutMs);
            }
        }
        catch (Exception ex) when (allowDeviceReset && IsDeviceLost(ex))
        {
            ResetDevice();
            return CaptureOnce(hwnd, timeoutMs, allowDeviceReset: false);
        }
    }

    private CapturedBitmap CaptureFromSession(CachedSession session, int timeoutMs)
    {
        if (!NativeMethods.IsWindow(session.Hwnd))
            throw new ComputerUseException(ErrorCodes.CaptureFailed, "Window is gone.");

        var size = session.Item.Size;
        if (size.Width <= 0 || size.Height <= 0)
            throw new ComputerUseException(ErrorCodes.EmptyFrame, "Windows Graphics Capture reported an empty size.");

        if (size.Width != session.Size.Width || size.Height != session.Size.Height)
        {
            session.Pool.Recreate(_winrtDevice!, DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, size);
            session.Size = size;
        }

        using var frame = WaitForFrame(session, timeoutMs);
        return CopyFrame(_d3dDevice, _d3dContext, frame);
    }

    private CachedSession OpenSession(nint hwnd)
    {
        EnsureDevice();
        var item = CreateItemForWindow(hwnd);
        if (item.Size.Width <= 0 || item.Size.Height <= 0)
            throw new ComputerUseException(ErrorCodes.EmptyFrame, "Windows Graphics Capture reported an empty size.");

        Direct3D11CaptureFramePool? pool = null;
        GraphicsCaptureSession? captureSession = null;
        var signal = new ManualResetEventSlim(false);
        try
        {
            pool = Direct3D11CaptureFramePool.Create(
                _winrtDevice!,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                1,
                item.Size);
            captureSession = pool.CreateCaptureSession(item);
            try { captureSession.IsCursorCaptureEnabled = false; } catch { /* older OS */ }
            TryDisableBorder(captureSession);

            TypedEventHandler<Direct3D11CaptureFramePool, object> handler = (_, _) =>
            {
                try { signal.Set(); } catch { /* disposed */ }
            };
            pool.FrameArrived += handler;
            captureSession.StartCapture();
            return new CachedSession
            {
                Hwnd = hwnd,
                Item = item,
                Pool = pool,
                Session = captureSession,
                Size = item.Size,
                Signal = signal,
                Handler = handler
            };
        }
        catch
        {
            if (captureSession is not null)
                Close(captureSession);
            if (pool is not null)
                Close(pool);
            signal.Dispose();
            throw;
        }
    }

    private Direct3D11CaptureFrame WaitForFrame(CachedSession session, int timeoutMs)
    {
        Pump();
        var existing = session.Pool.TryGetNextFrame();
        if (existing is not null)
            return existing;

        session.Signal.Reset();
        var deadline = Environment.TickCount64 + Math.Max(1, timeoutMs);
        var handles = new[] { session.Signal.WaitHandle.SafeWaitHandle.DangerousGetHandle() };

        while (true)
        {
            var remaining = deadline - Environment.TickCount64;
            if (remaining <= 0)
                throw new ComputerUseException(ErrorCodes.CaptureTimeout, "Windows Graphics Capture produced no frame before timeout.");

            var wait = NativeMethods.MsgWaitForMultipleObjectsEx(
                1,
                handles,
                (uint)Math.Min(remaining, int.MaxValue),
                NativeMethods.QS_ALLINPUT,
                NativeMethods.MWMO_INPUTAVAILABLE);
            if (wait == NativeMethods.WAIT_FAILED)
                throw new ComputerUseException(ErrorCodes.CaptureFailed, "Waiting for a capture frame failed.");

            Pump();
            var frame = session.Pool.TryGetNextFrame();
            if (frame is not null)
                return frame;
            if (wait == NativeMethods.WAIT_TIMEOUT)
                throw new ComputerUseException(ErrorCodes.CaptureTimeout, "Windows Graphics Capture produced no frame before timeout.");
        }
    }

    private void DropSession(nint hwnd)
    {
        if (_sessions.Remove(hwnd, out var session))
            CloseSession(session);
    }

    private static void CloseSession(CachedSession session)
    {
        try
        {
            if (session.Handler is not null)
                session.Pool.FrameArrived -= session.Handler;
        }
        catch { /* already closed */ }
        Close(session.Session);
        Close(session.Pool);
        session.Signal.Dispose();
    }

    private CapturedBitmap CopyFrame(nint device, nint context, Direct3D11CaptureFrame frame)
    {
        var access = frame.Surface.As<IDirect3DDxgiInterfaceAccess>();
        var iid = NativeMethods.IID_ID3D11Texture2D;
        var source = access.GetInterface(ref iid);
        if (source == 0)
            throw new ComputerUseException(ErrorCodes.CaptureFailed, "Could not access the capture texture.");
        try
        {
            EnsureGetDesc(source);
            GetTextureDesc(source, out var desc);
            if (desc.Width == 0 || desc.Height == 0)
                throw new ComputerUseException(ErrorCodes.EmptyFrame, "The captured texture is empty.");

            EnsureStaging(device, desc);
            CopySubresourceRegion(context, _staging, source);
            Marshal.ThrowExceptionForHR(Map(context, _staging, out var mapped));
            try
            {
                var width = (int)desc.Width;
                var height = (int)desc.Height;
                var rowBytes = width * 4;
                CapturedBitmap? captured = CapturedBitmap.Rent(width, height, rowBytes, "wgc");
                try
                {
                    for (var y = 0; y < height; y++)
                    {
                        Marshal.Copy(mapped.pData + y * (int)mapped.RowPitch, captured.Bgra, y * rowBytes, rowBytes);
                    }
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
                Unmap(context, _staging);
            }
        }
        finally
        {
            Marshal.Release(source);
        }
    }

    private void EnsureStaging(nint device, NativeMethods.D3D11_TEXTURE2D_DESC source)
    {
        if (_staging != 0
            && _stagingWidth >= source.Width
            && _stagingHeight >= source.Height
            && _stagingFormat == source.Format)
        {
            return;
        }

        var width = Math.Max(source.Width, _stagingWidth);
        var height = Math.Max(source.Height, _stagingHeight);
        if (width == 0)
            width = source.Width;
        if (height == 0)
            height = source.Height;

        var desc = source;
        desc.Width = width;
        desc.Height = height;
        desc.BindFlags = 0;
        desc.MiscFlags = 0;
        desc.Usage = NativeMethods.D3D11_USAGE_STAGING;
        desc.CPUAccessFlags = NativeMethods.D3D11_CPU_ACCESS_READ;
        desc.MipLevels = 1;
        desc.ArraySize = 1;
        desc.SampleCount = 1;
        desc.SampleQuality = 0;
        Marshal.ThrowExceptionForHR(CreateTexture2D(device, in desc, out var staging));
        ReleaseStaging();
        _staging = staging;
        _stagingWidth = width;
        _stagingHeight = height;
        _stagingFormat = source.Format;
    }

    private void EnsureDevice()
    {
        if (_d3dDevice != 0 && _winrtDevice is not null)
            return;
        ReleaseDevice();
        CreateD3D11(out _d3dDevice, out _d3dContext);
        _winrtDevice = CreateWinrtDevice(_d3dDevice);
        _createTexture2D = Marshal.GetDelegateForFunctionPointer<CreateTexture2DDelegate>(VTable(_d3dDevice, 5));
        _copySubresourceRegion = Marshal.GetDelegateForFunctionPointer<CopySubresourceRegionDelegate>(VTable(_d3dContext, 46));
        _map = Marshal.GetDelegateForFunctionPointer<MapDelegate>(VTable(_d3dContext, 14));
        _unmap = Marshal.GetDelegateForFunctionPointer<UnmapDelegate>(VTable(_d3dContext, 15));
    }

    private void ResetDevice()
    {
        foreach (var session in _sessions.Drain())
            CloseSession(session);
        ReleaseStaging();
        ReleaseDevice();
    }

    private void ReleaseStaging()
    {
        if (_staging == 0)
            return;
        Marshal.Release(_staging);
        _staging = 0;
        _stagingWidth = 0;
        _stagingHeight = 0;
        _stagingFormat = 0;
    }

    private void ReleaseDevice()
    {
        _createTexture2D = null;
        _copySubresourceRegion = null;
        _map = null;
        _unmap = null;
        _getDesc = null;
        if (_winrtDevice is IDisposable disposable)
        {
            try { disposable.Dispose(); } catch { }
        }
        _winrtDevice = null;
        if (_d3dContext != 0)
        {
            Marshal.Release(_d3dContext);
            _d3dContext = 0;
        }
        if (_d3dDevice != 0)
        {
            Marshal.Release(_d3dDevice);
            _d3dDevice = 0;
        }
    }

    private static GraphicsCaptureItem CreateItemForWindow(nint hwnd)
    {
        var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        var interop = factory.AsInterface<IGraphicsCaptureItemInterop>();
        var iid = NativeMethods.IID_IGraphicsCaptureItem;
        Marshal.ThrowExceptionForHR(interop.CreateForWindow(hwnd, ref iid, out var unk));
        try
        {
            return GraphicsCaptureItem.FromAbi(unk);
        }
        finally
        {
            Marshal.Release(unk);
        }
    }

    private static void CreateD3D11(out nint device, out nint context)
    {
        var hr = NativeMethods.D3D11CreateDevice(
            0,
            NativeMethods.D3D_DRIVER_TYPE_HARDWARE,
            0,
            NativeMethods.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            0,
            0,
            NativeMethods.D3D11_SDK_VERSION,
            out device,
            out _,
            out context);
        if (hr != 0 || device == 0)
        {
            hr = NativeMethods.D3D11CreateDevice(
                0,
                NativeMethods.D3D_DRIVER_TYPE_WARP,
                0,
                NativeMethods.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                0,
                0,
                NativeMethods.D3D11_SDK_VERSION,
                out device,
                out _,
                out context);
        }
        Marshal.ThrowExceptionForHR(hr);
    }

    private static IDirect3DDevice CreateWinrtDevice(nint d3dDevice)
    {
        var iid = NativeMethods.IID_IDXGIDevice;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3dDevice, in iid, out var dxgi));
        try
        {
            Marshal.ThrowExceptionForHR(NativeMethods.CreateDirect3D11DeviceFromDXGIDevice(dxgi, out var inspectable));
            try
            {
                return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
            }
            finally
            {
                Marshal.Release(inspectable);
            }
        }
        finally
        {
            Marshal.Release(dxgi);
        }
    }

    private static void TryDisableBorder(GraphicsCaptureSession session)
    {
        try
        {
            var prop = typeof(GraphicsCaptureSession).GetProperty("IsBorderRequired");
            prop?.SetValue(session, false);
        }
        catch
        {
            // optional
        }
    }

    private static void Close(IDisposable? obj)
    {
        try { obj?.Dispose(); } catch { }
    }

    private static void Pump()
    {
        while (NativeMethods.PeekMessage(out var msg, 0, 0, 0, NativeMethods.PM_REMOVE))
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }
    }

    private static bool IsAccessDenied(Exception ex) =>
        ex is UnauthorizedAccessException
        || (ex is COMException com && (unchecked((uint)com.HResult) is 0x80070005 or 0x887A0006 or 0x800704EC));

    private static bool IsDeviceLost(Exception ex) =>
        ex is COMException com && (unchecked((uint)com.HResult) is 0x887A0005 or 0x887A0007);

    private static nint VTable(nint com, int slot)
    {
        var vt = Marshal.ReadIntPtr(com);
        return Marshal.ReadIntPtr(vt, slot * nint.Size);
    }

    private void EnsureGetDesc(nint texture)
    {
        _getDesc ??= Marshal.GetDelegateForFunctionPointer<GetDescDelegate>(VTable(texture, 10));
    }

    private void GetTextureDesc(nint texture, out NativeMethods.D3D11_TEXTURE2D_DESC desc)
    {
        (_getDesc ?? throw new InvalidOperationException("D3D GetDesc is not bound.")).Invoke(texture, out desc);
    }

    private int CreateTexture2D(nint device, in NativeMethods.D3D11_TEXTURE2D_DESC desc, out nint texture)
    {
        return (_createTexture2D ?? throw new InvalidOperationException("D3D CreateTexture2D is not bound.")).Invoke(device, in desc, 0, out texture);
    }

    private void CopySubresourceRegion(nint context, nint dst, nint src)
    {
        (_copySubresourceRegion ?? throw new InvalidOperationException("D3D CopySubresourceRegion is not bound."))
            .Invoke(context, dst, 0, 0, 0, 0, src, 0, 0);
    }

    private int Map(nint context, nint resource, out NativeMethods.D3D11_MAPPED_SUBRESOURCE mapped)
    {
        return (_map ?? throw new InvalidOperationException("D3D Map is not bound."))
            .Invoke(context, resource, 0, NativeMethods.D3D11_MAP_READ, 0, out mapped);
    }

    private void Unmap(nint context, nint resource)
    {
        (_unmap ?? throw new InvalidOperationException("D3D Unmap is not bound.")).Invoke(context, resource, 0);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GetDescDelegate(nint self, out NativeMethods.D3D11_TEXTURE2D_DESC desc);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateTexture2DDelegate(nint self, in NativeMethods.D3D11_TEXTURE2D_DESC desc, nint initial, out nint texture);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void CopySubresourceRegionDelegate(
        nint self,
        nint dst,
        uint dstSubresource,
        uint dstX,
        uint dstY,
        uint dstZ,
        nint src,
        uint srcSubresource,
        nint srcBox);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int MapDelegate(nint self, nint resource, uint sub, uint map, uint flags, out NativeMethods.D3D11_MAPPED_SUBRESOURCE mapped);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void UnmapDelegate(nint self, nint resource, uint sub);

    private sealed class CachedSession
    {
        public required nint Hwnd { get; init; }
        public required GraphicsCaptureItem Item { get; init; }
        public required Direct3D11CaptureFramePool Pool { get; init; }
        public required GraphicsCaptureSession Session { get; init; }
        public required ManualResetEventSlim Signal { get; init; }
        public required TypedEventHandler<Direct3D11CaptureFramePool, object> Handler { get; init; }
        public SizeInt32 Size { get; set; }
    }
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
internal interface IGraphicsCaptureItemInterop
{
    [PreserveSig]
    int CreateForWindow(nint window, ref Guid iid, out nint result);
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("A9B3D012-3BAD-423B-8A22-0D83905A3A32")]
internal interface IDirect3DDxgiInterfaceAccess
{
    nint GetInterface(ref Guid iid);
}

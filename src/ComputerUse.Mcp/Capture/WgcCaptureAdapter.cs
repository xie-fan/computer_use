using System.Runtime.InteropServices;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Native;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace ComputerUse.Mcp.Capture;

internal sealed class WgcCaptureAdapter
{
    public CapturedBitmap Capture(nint hwnd, int timeoutMs)
    {
        if (!NativeMethods.IsWindow(hwnd))
            throw new ComputerUseException(ErrorCodes.CaptureFailed, "Window is gone.");

        nint d3dDevice = 0;
        nint d3dContext = 0;
        IDirect3DDevice? winrtDevice = null;
        GraphicsCaptureItem? item = null;
        Direct3D11CaptureFramePool? pool = null;
        GraphicsCaptureSession? session = null;
        Direct3D11CaptureFrame? frame = null;
        try
        {
            CreateD3D11(out d3dDevice, out d3dContext);
            winrtDevice = CreateWinrtDevice(d3dDevice);
            item = CreateItemForWindow(hwnd);
            if (item.Size.Width <= 0 || item.Size.Height <= 0)
                throw new ComputerUseException(ErrorCodes.EmptyFrame, "Windows Graphics Capture reported an empty size.");

            pool = Direct3D11CaptureFramePool.Create(
                winrtDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                1,
                item.Size);
            session = pool.CreateCaptureSession(item);
            try { session.IsCursorCaptureEnabled = false; } catch { /* older OS */ }
            TryDisableBorder(session);

            Direct3D11CaptureFrame? arrived = null;
            pool.FrameArrived += (_, _) =>
            {
                arrived ??= pool.TryGetNextFrame();
            };
            session.StartCapture();

            var deadline = Environment.TickCount64 + timeoutMs;
            while (arrived is null && Environment.TickCount64 < deadline)
            {
                Pump();
                Thread.Sleep(8);
            }

            if (arrived is null)
                throw new ComputerUseException(ErrorCodes.CaptureTimeout, "Windows Graphics Capture produced no frame before timeout.");

            frame = arrived;
            return CopyFrame(d3dDevice, d3dContext, frame);
        }
        catch (ComputerUseException)
        {
            throw;
        }
        catch (Exception ex) when (IsAccessDenied(ex))
        {
            throw new ComputerUseException(ErrorCodes.ProtectedContent, "The window content is protected.");
        }
        catch (Exception)
        {
            throw new ComputerUseException(ErrorCodes.CaptureUnsupported, "Windows Graphics Capture cannot capture this window.");
        }
        finally
        {
            frame?.Dispose();
            Close(session);
            Close(pool);
            if (winrtDevice is IDisposable d)
                d.Dispose();
            if (d3dContext != 0)
                Marshal.Release(d3dContext);
            if (d3dDevice != 0)
                Marshal.Release(d3dDevice);
        }
    }

    private static CapturedBitmap CopyFrame(nint device, nint context, Direct3D11CaptureFrame frame)
    {
        var access = frame.Surface.As<IDirect3DDxgiInterfaceAccess>();
        var iid = NativeMethods.IID_ID3D11Texture2D;
        var source = access.GetInterface(ref iid);
        if (source == 0)
            throw new ComputerUseException(ErrorCodes.CaptureFailed, "Could not access the capture texture.");
        nint staging = 0;
        try
        {
            var desc = new NativeMethods.D3D11_TEXTURE2D_DESC();
            GetTextureDesc(source, out desc);
            if (desc.Width == 0 || desc.Height == 0)
                throw new ComputerUseException(ErrorCodes.EmptyFrame, "The captured texture is empty.");

            desc.BindFlags = 0;
            desc.MiscFlags = 0;
            desc.Usage = NativeMethods.D3D11_USAGE_STAGING;
            desc.CPUAccessFlags = NativeMethods.D3D11_CPU_ACCESS_READ;
            desc.MipLevels = 1;
            desc.ArraySize = 1;
            desc.SampleCount = 1;
            desc.SampleQuality = 0;
            Marshal.ThrowExceptionForHR(CreateTexture2D(device, in desc, out staging));
            CopyResource(context, staging, source);
            Marshal.ThrowExceptionForHR(Map(context, staging, out var mapped));
            try
            {
                var width = (int)desc.Width;
                var height = (int)desc.Height;
                var stride = (int)mapped.RowPitch;
                var bgra = new byte[stride * height];
                Marshal.Copy(mapped.pData, bgra, 0, bgra.Length);
                return new CapturedBitmap
                {
                    Bgra = bgra,
                    Width = width,
                    Height = height,
                    Stride = stride,
                    Method = "wgc"
                };
            }
            finally
            {
                Unmap(context, staging);
            }
        }
        finally
        {
            if (staging != 0)
                Marshal.Release(staging);
            Marshal.Release(source);
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
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3dDevice, ref iid, out var dxgi));
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

    private static nint VTable(nint com, int slot)
    {
        var vt = Marshal.ReadIntPtr(com);
        return Marshal.ReadIntPtr(vt, slot * nint.Size);
    }

    private static void GetTextureDesc(nint texture, out NativeMethods.D3D11_TEXTURE2D_DESC desc)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<GetDescDelegate>(VTable(texture, 10));
        fn(texture, out desc);
    }

    private static int CreateTexture2D(nint device, in NativeMethods.D3D11_TEXTURE2D_DESC desc, out nint texture)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<CreateTexture2DDelegate>(VTable(device, 5));
        return fn(device, in desc, 0, out texture);
    }

    private static void CopyResource(nint context, nint dst, nint src)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<CopyResourceDelegate>(VTable(context, 47));
        fn(context, dst, src);
    }

    private static int Map(nint context, nint resource, out NativeMethods.D3D11_MAPPED_SUBRESOURCE mapped)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<MapDelegate>(VTable(context, 14));
        return fn(context, resource, 0, NativeMethods.D3D11_MAP_READ, 0, out mapped);
    }

    private static void Unmap(nint context, nint resource)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<UnmapDelegate>(VTable(context, 15));
        fn(context, resource, 0);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GetDescDelegate(nint self, out NativeMethods.D3D11_TEXTURE2D_DESC desc);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateTexture2DDelegate(nint self, in NativeMethods.D3D11_TEXTURE2D_DESC desc, nint initial, out nint texture);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void CopyResourceDelegate(nint self, nint dst, nint src);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int MapDelegate(nint self, nint resource, uint sub, uint map, uint flags, out NativeMethods.D3D11_MAPPED_SUBRESOURCE mapped);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void UnmapDelegate(nint self, nint resource, uint sub);
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

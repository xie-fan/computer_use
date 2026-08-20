using ComputerUse.Mcp.Capture;
using ComputerUse.Mcp.Domain;

namespace ComputerUse.Mcp.Tests;

public sealed class BgraEmptyFrameTests
{
    [Fact]
    public void AllZero_IsEmpty()
    {
        var bgra = new byte[8 * 4 * 4];
        Assert.True(BgraEmptyFrame.IsEmpty(bgra, 8, 4, 32));
    }

    [Fact]
    public void PixelOnUnsampledRow_IsNotEmptyAfterFullScan()
    {
        var width = 4;
        var height = 16;
        var stride = width * 4;
        var bgra = new byte[stride * height];
        var i = 7 * stride;
        bgra[i] = 12;
        Assert.False(BgraEmptyFrame.IsEmpty(bgra, width, height, stride, sampleRowStep: 8));
    }

    [Fact]
    public void SampledRgbPixel_IsNotEmpty()
    {
        var bgra = new byte[8 * 8 * 4];
        bgra[2] = 255;
        Assert.False(BgraEmptyFrame.IsEmpty(bgra, 8, 8, 32));
    }
}

public sealed class PrintWindowBgraCodecTests
{
    [Fact]
    public void RoundTrip_PreservesPixelsAndDoesNotUseTempPng()
    {
        var width = 3;
        var height = 2;
        var stride = 12;
        var src = CapturedBitmap.Rent(width, height, stride, "print_window");
        try
        {
            src.Bgra[0] = 1;
            src.Bgra[5] = 9;
            src.Bgra[11] = 7;
            using var ms = new MemoryStream();
            PrintWindowBgraCodec.Write(ms, src);
            ms.Position = 0;
            var copy = PrintWindowBgraCodec.Read(ms);
            try
            {
                Assert.Equal(width, copy.Width);
                Assert.Equal(height, copy.Height);
                Assert.Equal(stride, copy.Stride);
                Assert.Equal("print_window", copy.Method);
                Assert.Equal(1, copy.Bgra[0]);
                Assert.Equal(9, copy.Bgra[5]);
                Assert.Equal(7, copy.Bgra[11]);
            }
            finally
            {
                copy.Return();
            }
        }
        finally
        {
            src.Return();
        }
    }

    [Fact]
    public void BadMagic_IsCaptureFailed()
    {
        using var ms = new MemoryStream([0x00, 0x01, 0x02, 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        var ex = Assert.Throws<ComputerUseException>(() => PrintWindowBgraCodec.Read(ms));
        Assert.Equal(ErrorCodes.CaptureFailed, ex.Code);
    }
}

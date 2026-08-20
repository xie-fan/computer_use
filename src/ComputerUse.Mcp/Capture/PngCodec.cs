using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ComputerUse.Mcp.Domain;

namespace ComputerUse.Mcp.Capture;

internal static class PngCodec
{
    private const double ShrinkFactor = 0.85;
    private const int MaxShrinkAttempts = 8;

    public static byte[] EncodeBgra(byte[] bgra, int width, int height, int stride)
    {
        using var bmp = FromBgra(bgra, width, height, stride);
        return SavePng(bmp);
    }

    public static (byte[] Png, int Width, int Height, double Scale) FitLongEdge(
        byte[] bgra, int width, int height, int stride, int maxLongEdge, int maxPngBytes)
    {
        var longEdge = Math.Max(width, height);
        if (longEdge <= 0)
            throw new ComputerUseException(ErrorCodes.EmptyFrame, "Capture produced an empty bitmap.");

        using var src = FromBgra(bgra, width, height, stride);
        var scale = longEdge <= maxLongEdge ? 1.0 : maxLongEdge / (double)longEdge;

        for (var attempt = 0; attempt < MaxShrinkAttempts; attempt++)
        {
            var outW = Math.Max(1, (int)Math.Floor(width * scale));
            var outH = Math.Max(1, (int)Math.Floor(height * scale));
            var mappedScale = Math.Min(outW / (double)width, outH / (double)height);

            byte[] png;
            if (outW == width && outH == height)
            {
                png = SavePng(src);
                if (png.Length <= maxPngBytes)
                    return (png, width, height, 1.0);
            }
            else
            {
                using var scaled = ScaleTo(src, outW, outH);
                png = SavePng(scaled);
                if (png.Length <= maxPngBytes)
                    return (png, outW, outH, mappedScale);
            }

            if (outW <= 1 && outH <= 1)
                break;
            scale = mappedScale * ShrinkFactor;
        }

        throw new ComputerUseException(ErrorCodes.PayloadTooLarge, "The PNG exceeds maxPngBytes.");
    }

    private static Bitmap ScaleTo(Bitmap src, int outW, int outH)
    {
        var dest = new Bitmap(outW, outH, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(dest);
        g.InterpolationMode = InterpolationMode.Bilinear;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(src, 0, 0, outW, outH);
        return dest;
    }

    private static byte[] SavePng(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static Bitmap FromBgra(byte[] bgra, int width, int height, int stride)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = Math.Min(width * 4, Math.Min(stride, data.Stride));
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(bgra, y * stride, data.Scan0 + y * data.Stride, rowBytes);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return bmp;
    }
}

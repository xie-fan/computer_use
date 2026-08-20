using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ComputerUse.Mcp.Capture;

internal static class PngCodec
{
    public static byte[] EncodeBgra(byte[] bgra, int width, int height, int stride)
    {
        using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < height; y++)
            {
                var src = y * stride;
                var dst = data.Scan0 + y * data.Stride;
                var bytes = Math.Min(width * 4, Math.Min(stride, data.Stride));
                Marshal.Copy(bgra, src, dst, bytes);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    public static (byte[] Png, int Width, int Height, double Scale) FitLongEdge(
        byte[] bgra, int width, int height, int stride, int maxLongEdge, int maxPngBytes)
    {
        var longEdge = Math.Max(width, height);
        if (longEdge <= 0)
            throw new Domain.ComputerUseException(Domain.ErrorCodes.EmptyFrame, "Capture produced an empty bitmap.");

        Bitmap working;
        double scale;
        if (longEdge <= maxLongEdge)
        {
            working = FromBgra(bgra, width, height, stride);
            scale = 1.0;
        }
        else
        {
            scale = maxLongEdge / (double)longEdge;
            var outW = Math.Max(1, (int)Math.Floor(width * scale));
            var outH = Math.Max(1, (int)Math.Floor(height * scale));
            scale = Math.Min(outW / (double)width, outH / (double)height);
            using var src = FromBgra(bgra, width, height, stride);
            working = new Bitmap(outW, outH, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(working);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(src, 0, 0, outW, outH);
        }

        using (working)
        {
            using var ms = new MemoryStream();
            working.Save(ms, ImageFormat.Png);
            var png = ms.ToArray();
            if (png.Length > maxPngBytes)
            {
                throw new Domain.ComputerUseException(
                    Domain.ErrorCodes.PayloadTooLarge,
                    "The PNG exceeds maxPngBytes.");
            }
            return (png, working.Width, working.Height, scale);
        }
    }

    private static Bitmap FromBgra(byte[] bgra, int width, int height, int stride)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(bgra, y * stride, data.Scan0 + y * data.Stride, Math.Min(width * 4, Math.Min(stride, data.Stride)));
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return bmp;
    }
}

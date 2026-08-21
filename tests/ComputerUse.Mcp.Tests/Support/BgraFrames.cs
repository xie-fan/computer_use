namespace ComputerUse.Mcp.Tests.Support;

internal static class BgraFrames
{
    public static byte[] Solid(int width, int height, byte b, byte g, byte r, byte a = 255)
    {
        var data = new byte[checked(width * height * 4)];
        for (var i = 0; i < data.Length; i += 4)
        {
            data[i] = b;
            data[i + 1] = g;
            data[i + 2] = r;
            data[i + 3] = a;
        }

        return data;
    }

    public static byte[] Noise(int width, int height, int seed = 11)
    {
        var data = new byte[checked(width * height * 4)];
        for (var i = 0; i < data.Length; i++)
            data[i] = (byte)(i * 37 + seed);
        return data;
    }

    public static byte[] Checker(int width, int height, int cell = 4, byte lo = 0, byte hi = 255)
    {
        var data = new byte[checked(width * height * 4)];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var on = ((x / cell) + (y / cell)) % 2 == 0;
                var i = (y * width + x) * 4;
                var v = on ? hi : lo;
                data[i] = v;
                data[i + 1] = v;
                data[i + 2] = v;
                data[i + 3] = 255;
            }
        }

        return data;
    }

    public static void Paste(byte[] dest, int destW, byte[] src, int srcW, int srcH, int x, int y)
    {
        for (var row = 0; row < srcH; row++)
        {
            var destOff = ((y + row) * destW + x) * 4;
            var srcOff = row * srcW * 4;
            Buffer.BlockCopy(src, srcOff, dest, destOff, srcW * 4);
        }
    }

    public static byte[] Crop(byte[] src, int srcW, int x, int y, int w, int h)
    {
        var dest = new byte[w * h * 4];
        for (var row = 0; row < h; row++)
            Buffer.BlockCopy(src, ((y + row) * srcW + x) * 4, dest, row * w * 4, w * 4);
        return dest;
    }

    public static byte[] AddChannelOffset(byte[] src, int delta)
    {
        var dest = (byte[])src.Clone();
        for (var i = 0; i < dest.Length; i += 4)
        {
            dest[i] = Clamp(dest[i] + delta);
            dest[i + 1] = Clamp(dest[i + 1] + delta);
            dest[i + 2] = Clamp(dest[i + 2] + delta);
        }

        return dest;
    }

    public static byte[] ScaleNearest(byte[] src, int srcW, int srcH, int factor)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(factor, 1);
        var destW = checked(srcW * factor);
        var destH = checked(srcH * factor);
        var dest = new byte[checked(destW * destH * 4)];
        for (var y = 0; y < srcH; y++)
        {
            for (var fy = 0; fy < factor; fy++)
            {
                var destRow = ((y * factor) + fy) * destW;
                for (var x = 0; x < srcW; x++)
                {
                    var srcOff = (y * srcW + x) * 4;
                    for (var fx = 0; fx < factor; fx++)
                    {
                        var destOff = (destRow + x * factor + fx) * 4;
                        dest[destOff] = src[srcOff];
                        dest[destOff + 1] = src[srcOff + 1];
                        dest[destOff + 2] = src[srcOff + 2];
                        dest[destOff + 3] = src[srcOff + 3];
                    }
                }
            }
        }

        return dest;
    }

    private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);
}

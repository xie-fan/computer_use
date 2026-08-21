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

    public static byte[] Checker(int width, int height, int cell = 4)
    {
        var data = new byte[checked(width * height * 4)];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var on = ((x / cell) + (y / cell)) % 2 == 0;
                var i = (y * width + x) * 4;
                var v = on ? (byte)255 : (byte)0;
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
}

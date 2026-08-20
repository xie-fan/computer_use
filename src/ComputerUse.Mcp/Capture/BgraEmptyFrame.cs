using ComputerUse.Mcp.Domain;

namespace ComputerUse.Mcp.Capture;

internal static class BgraEmptyFrame
{
    public const int SampleRowStep = 8;

    public static bool IsEmpty(byte[] bgra, int width, int height, int stride) =>
        IsEmpty(bgra, width, height, stride, SampleRowStep);

    public static bool IsEmpty(byte[] bgra, int width, int height, int stride, int sampleRowStep)
    {
        if (width <= 0 || height <= 0 || stride < width * 4)
            return true;
        if (!SampleRgbRowsEmpty(bgra, width, height, stride, Math.Max(1, sampleRowStep)))
            return false;
        return AllBytesEmpty(bgra, width, height, stride);
    }

    private static bool SampleRgbRowsEmpty(byte[] bgra, int width, int height, int stride, int step)
    {
        for (var y = 0; y < height; y += step)
        {
            var row = y * stride;
            for (var x = 0; x < width; x++)
            {
                var i = row + x * 4;
                if (bgra[i] != 0 || bgra[i + 1] != 0 || bgra[i + 2] != 0)
                    return false;
            }
        }

        return true;
    }

    private static bool AllBytesEmpty(byte[] bgra, int width, int height, int stride)
    {
        var rowBytes = width * 4;
        for (var y = 0; y < height; y++)
        {
            var row = y * stride;
            for (var i = 0; i < rowBytes; i++)
            {
                if (bgra[row + i] != 0)
                    return false;
            }
        }

        return true;
    }
}

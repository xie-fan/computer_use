using ComputerUse.Mcp.Domain;

namespace ComputerUse.Mcp.Vision;

internal static class CropEntropy
{
    private const int MinLumaRange = 16;
    private const double MinLumaVariance = 64.0;

    public static byte[] ExtractValidated(
        byte[] bgra,
        int width,
        int height,
        int stride,
        int x,
        int y,
        int cropWidth,
        int cropHeight)
    {
        ArgumentNullException.ThrowIfNull(bgra);

        var minEdge = Limits.V1.MinCropEdgePx;
        if (cropWidth < minEdge || cropHeight < minEdge)
            throw LowEntropy("The crop is smaller than the minimum 24×24 edge.");

        if (x < 0 || y < 0 || width <= 0 || height <= 0
            || (long)x + cropWidth > width
            || (long)y + cropHeight > height)
            throw LowEntropy("The crop is outside the frame.");

        if (stride < checked(width * 4))
            throw LowEntropy("The frame stride is too small to contain the crop.");

        var lastExclusive = (long)(y + cropHeight - 1) * stride + (long)(x + cropWidth) * 4;
        if (lastExclusive > bgra.Length)
            throw LowEntropy("The crop is outside the frame.");

        var packed = CopyPacked(bgra, stride, x, y, cropWidth, cropHeight);
        if (HasLowVariance(packed, cropWidth, cropHeight))
            throw LowEntropy("The crop is blank or has too little visual variance.");

        return packed;
    }

    private static byte[] CopyPacked(
        byte[] bgra, int stride, int x, int y, int cropWidth, int cropHeight)
    {
        var rowBytes = checked(cropWidth * 4);
        var dest = new byte[checked(rowBytes * cropHeight)];
        var srcOrigin = checked(y * stride + x * 4);
        for (var row = 0; row < cropHeight; row++)
        {
            Buffer.BlockCopy(
                bgra,
                checked(srcOrigin + row * stride),
                dest,
                checked(row * rowBytes),
                rowBytes);
        }

        return dest;
    }

    private static bool HasLowVariance(byte[] packed, int cropWidth, int cropHeight)
    {
        var n = checked(cropWidth * cropHeight);
        var minLuma = 255;
        var maxLuma = 0;
        double sum = 0;
        double sumSq = 0;

        for (var i = 0; i < packed.Length; i += 4)
        {
            var luma = (77 * packed[i + 2] + 150 * packed[i + 1] + 29 * packed[i]) >> 8;
            if (luma < minLuma)
                minLuma = luma;
            if (luma > maxLuma)
                maxLuma = luma;
            sum += luma;
            sumSq += (double)luma * luma;
        }

        if (maxLuma - minLuma < MinLumaRange)
            return true;

        var mean = sum / n;
        var variance = sumSq / n - mean * mean;
        return variance < MinLumaVariance;
    }

    private static ComputerUseException LowEntropy(string message) =>
        new(ErrorCodes.LowEntropyCrop, message);
}

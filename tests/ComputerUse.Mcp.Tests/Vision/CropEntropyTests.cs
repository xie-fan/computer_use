using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Tests.Support;
using ComputerUse.Mcp.Vision;

namespace ComputerUse.Mcp.Tests.Vision;

public sealed class CropEntropyTests
{
    [Fact]
    public void CropSmallerThan24_IsLowEntropyCrop()
    {
        var bgra = BgraFrames.Noise(40, 40);
        var ex = Assert.Throws<ComputerUseException>(() =>
            CropEntropy.ExtractValidated(bgra, 40, 40, 160, 0, 0, 23, 24));
        Assert.Equal(ErrorCodes.LowEntropyCrop, ex.Code);
    }

    [Fact]
    public void SolidColorCrop_IsLowEntropyCrop()
    {
        var bgra = BgraFrames.Solid(32, 32, 80, 80, 80);
        var ex = Assert.Throws<ComputerUseException>(() =>
            CropEntropy.ExtractValidated(bgra, 32, 32, 128, 0, 0, 32, 32));
        Assert.Equal(ErrorCodes.LowEntropyCrop, ex.Code);
    }

    [Fact]
    public void HighVarianceCrop_Succeeds()
    {
        var bgra = BgraFrames.Checker(32, 32);
        var crop = CropEntropy.ExtractValidated(bgra, 32, 32, 128, 0, 0, 32, 32);
        Assert.Equal(32 * 32 * 4, crop.Length);
    }

    [Fact]
    public void CropHeightSmallerThan24_IsLowEntropyCrop()
    {
        var bgra = BgraFrames.Noise(40, 40);
        var ex = Assert.Throws<ComputerUseException>(() =>
            CropEntropy.ExtractValidated(bgra, 40, 40, 160, 0, 0, 24, 23));
        Assert.Equal(ErrorCodes.LowEntropyCrop, ex.Code);
    }

    [Fact]
    public void CropOutsideFrame_IsLowEntropyCrop()
    {
        var bgra = BgraFrames.Checker(32, 32);
        var ex = Assert.Throws<ComputerUseException>(() =>
            CropEntropy.ExtractValidated(bgra, 32, 32, 128, 16, 8, 24, 24));
        Assert.Equal(ErrorCodes.LowEntropyCrop, ex.Code);
    }

    [Fact]
    public void HighVarianceNoiseCrop_Succeeds()
    {
        var bgra = BgraFrames.Noise(32, 32);
        var crop = CropEntropy.ExtractValidated(bgra, 32, 32, 128, 0, 0, 32, 32);
        Assert.Equal(32 * 32 * 4, crop.Length);
    }

    [Fact]
    public void ExtractedCrop_UsesCompactStride()
    {
        var packed = BgraFrames.Checker(32, 32);
        const int stride = 32 * 4 + 16;
        var padded = new byte[32 * stride];
        for (var y = 0; y < 32; y++)
            Buffer.BlockCopy(packed, y * 128, padded, y * stride, 128);

        var crop = CropEntropy.ExtractValidated(padded, 32, 32, stride, 0, 0, 32, 32);
        Assert.Equal(32 * 32 * 4, crop.Length);
        Assert.Equal(packed, crop);
    }
}

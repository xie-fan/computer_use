using ComputerUse.Mcp.Tests.Support;
using ComputerUse.Mcp.Vision;

namespace ComputerUse.Mcp.Tests.Vision;

public sealed class PerceptualHashTests
{
    [Fact]
    public void IdenticalFrames_DistanceZero()
    {
        var frame = BgraFrames.Checker(64, 64);
        var a = PerceptualHash.Compute(frame, 64, 64, 256);
        var b = PerceptualHash.Compute(frame, 64, 64, 256);
        Assert.Equal(0, a.HammingDistance(b));
    }

    [Fact]
    public void UnrelatedFrames_AreFar()
    {
        var solid = BgraFrames.Solid(64, 64, 0, 0, 0);
        var noise = BgraFrames.Noise(64, 64);
        var a = PerceptualHash.Compute(solid, 64, 64, 256);
        var b = PerceptualHash.Compute(noise, 64, 64, 256);
        Assert.True(a.HammingDistance(b) > 8);
    }

    [Fact]
    public void Nominate_ReturnsAtMostThree()
    {
        var query = PerceptualHash.Compute(BgraFrames.Checker(32, 32), 32, 32, 128);
        var library = new List<(string Id, PerceptualHashValue Hash)>();
        for (var i = 0; i < 8; i++)
        {
            var bits = BgraFrames.Noise(32, 32, seed: 3 + i * 17);
            library.Add(("s" + i, PerceptualHash.Compute(bits, 32, 32, 128)));
        }

        var nominated = PerceptualHash.Nominate(query, library, maxCandidates: 3);
        Assert.InRange(nominated.Count, 1, 3);
        for (var i = 1; i < nominated.Count; i++)
            Assert.True(nominated[i - 1].Distance <= nominated[i].Distance);
    }
}

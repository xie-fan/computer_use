using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Identity;
using ComputerUse.Mcp.Tests.Support;

namespace ComputerUse.Mcp.Tests;

public sealed class CoordinateMapperTests
{
    [Fact]
    public void ScaleHalf_MapsImagePixelToSourceThenScreen()
    {
        var frame = TestFrames.Create(
            width: 5,
            height: 2,
            scale: 0.5,
            sourceWidth: 10,
            sourceHeight: 4,
            captureOrigin: new ScreenPoint(100, 200));

        var mapped = CoordinateMapper.MapImageToScreen(frame, 4, 1);
        Assert.Equal(108, mapped.X);
        Assert.Equal(202, mapped.Y);
    }

    [Fact]
    public void ScaleOne_IsIdentityOnSource()
    {
        var frame = TestFrames.Create(10, 4, captureOrigin: new ScreenPoint(7, 9));
        var mapped = CoordinateMapper.MapImageToScreen(frame, 4, 1);
        Assert.Equal(11, mapped.X);
        Assert.Equal(10, mapped.Y);
    }
}

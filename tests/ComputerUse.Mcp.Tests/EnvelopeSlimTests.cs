using System.Text.Json;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Mcp;

namespace ComputerUse.Mcp.Tests;

public sealed class EnvelopeSlimTests
{
    [Fact]
    public void OperateOutcome_OmitsLimitsAndCapabilities()
    {
        var json = JsonSerializer.Serialize(new OperateOutcome { CompletedCount = 1 }, ToolResults.Json);
        Assert.Contains("\"contractVersion\"", json);
        Assert.DoesNotContain("\"limits\"", json);
        Assert.DoesNotContain("\"capabilities\"", json);
    }

    [Fact]
    public void ScreenshotResult_OmitsLimitsAndCapabilities()
    {
        var json = JsonSerializer.Serialize(new ScreenshotResult
        {
            FrameId = "fr1.x",
            TargetToken = "tok",
            CaptureMethod = "wgc",
            Transform = new { rounding = "floor" },
            Dpi = new { x = 96, y = 96 },
            Bounds = new { left = 0, top = 0, width = 10, height = 10 },
            Monitor = new { deviceName = @"\\.\DISPLAY1" },
            SideEffects = new SideEffects()
        }, ToolResults.Json);
        Assert.Contains("\"contractVersion\"", json);
        Assert.DoesNotContain("\"limits\"", json);
        Assert.DoesNotContain("\"capabilities\"", json);
    }

    [Fact]
    public void ListWindows_KeepsFullEnvelope()
    {
        var json = JsonSerializer.SerializeToElement(new
        {
            contractVersion = Contract.Version,
            capabilities = new { virtualDesktop = new { membershipQuery = true, switching = false } },
            limits = Limits.V1.ToPublicDto()
        }, ToolResults.Json);
        Assert.True(json.TryGetProperty("limits", out _));
        Assert.True(json.TryGetProperty("capabilities", out _));
    }
}

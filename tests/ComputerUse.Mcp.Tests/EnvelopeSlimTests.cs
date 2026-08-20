using System.Text.Json;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Mcp;

namespace ComputerUse.Mcp.Tests;

public sealed class EnvelopeSlimTests
{
    [Fact]
    public void ToolResultsOk_OperateOutcome_OmitsLimitsAndUsesSourceGen()
    {
        var json = ToolResults.SerializeStructured(new OperateOutcome { CompletedCount = 1 }).GetRawText();
        Assert.Contains("\"contractVersion\"", json);
        Assert.Contains("\"completedCount\"", json);
        Assert.DoesNotContain("\"limits\"", json);
        Assert.DoesNotContain("\"capabilities\"", json);
    }

    [Fact]
    public void ToolResultsOk_ScreenshotResult_SerializesNamedDtos()
    {
        using var doc = JsonDocument.Parse(ToolResults.SerializeStructured(SampleScreenshot()).GetRawText());
        var root = doc.RootElement;
        Assert.Equal("fr1.x", root.GetProperty("frameId").GetString());
        Assert.Equal("floor", root.GetProperty("transform").GetProperty("rounding").GetString());
        Assert.Equal(96u, root.GetProperty("dpi").GetProperty("x").GetUInt32());
        Assert.Equal(10, root.GetProperty("bounds").GetProperty("width").GetInt32());
        Assert.Equal(@"\\.\DISPLAY1", root.GetProperty("monitor").GetProperty("deviceName").GetString());
        Assert.False(root.TryGetProperty("limits", out _));
        Assert.False(root.TryGetProperty("capabilities", out _));
    }

    [Fact]
    public void ToolResultsError_AnonymousSideEffectsDetails_Serializes()
    {
        var ex = new ComputerUseException(
            ErrorCodes.CaptureFailed,
            "Capture failed.",
            new { sideEffects = new SideEffects { WindowRestored = true } });
        using var doc = JsonDocument.Parse(ToolResults.SerializeError(ex).GetRawText());
        Assert.Equal("capture_failed", doc.RootElement.GetProperty("code").GetString());
        Assert.True(doc.RootElement.GetProperty("details").GetProperty("sideEffects").GetProperty("windowRestored").GetBoolean());
    }

    [Fact]
    public void ToolResultsError_OperateOutcomeDetails_Serializes()
    {
        var details = new OperateOutcome
        {
            CompletedCount = 2,
            FailedIndex = 2,
            OutcomeKnown = true,
            MayHaveExecuted = true,
            Code = ErrorCodes.FocusLost,
            SideEffects = new SideEffects { ForegroundChanged = true }
        };
        using var doc = JsonDocument.Parse(ToolResults.SerializeError(new ComputerUseException(ErrorCodes.FocusLost, "Foreground lost.", details)).GetRawText());
        var nested = doc.RootElement.GetProperty("details");
        Assert.Equal(2, nested.GetProperty("completedCount").GetInt32());
        Assert.Equal("focus_lost", nested.GetProperty("code").GetString());
        Assert.True(nested.GetProperty("sideEffects").GetProperty("foregroundChanged").GetBoolean());
    }

    [Fact]
    public void ToolResultsOk_OperateOutcome_WithWarningDetails_Serializes()
    {
        var json = ToolResults.SerializeStructured(new OperateOutcome
        {
            CompletedCount = 1,
            Warnings =
            [
                new WarningItem
                {
                    Code = "process_name_unavailable",
                    Message = "Process name could not be resolved.",
                    Details = EnvelopeJson.Details(new { pid = 42, targetToken = "tok" })
                }
            ]
        }).GetRawText();
        using var doc = JsonDocument.Parse(json);
        var details = doc.RootElement.GetProperty("warnings")[0].GetProperty("details");
        Assert.Equal(42, details.GetProperty("pid").GetInt32());
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

    private static ScreenshotResult SampleScreenshot() => new()
    {
        FrameId = "fr1.x",
        TargetToken = "tok",
        Width = 10,
        Height = 10,
        SourceWidth = 10,
        SourceHeight = 10,
        Scale = 1,
        CaptureMethod = "wgc",
        Transform = new TransformDto
        {
            Rounding = "floor",
            Scale = 1,
            CaptureOriginScreen = new PointDto { X = 0, Y = 0 },
            CaptureSize = new SizeDto { Width = 10, Height = 10 },
            WindowRect = new RectDto { Left = 0, Top = 0, Width = 10, Height = 10 },
            ExtendedFrameBounds = new RectDto { Left = 0, Top = 0, Width = 10, Height = 10 }
        },
        Dpi = new DpiDto { X = 96, Y = 96 },
        Bounds = new RectDto { Left = 0, Top = 0, Width = 10, Height = 10 },
        Monitor = new MonitorRefDto { DeviceName = @"\\.\DISPLAY1" },
        SideEffects = new SideEffects()
    };
}

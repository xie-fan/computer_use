using System.ComponentModel;
using System.Reflection;
using ComputerUse.Mcp.Mcp;
using ModelContextProtocol.Server;

namespace ComputerUse.Mcp.Tests;

public sealed class ControlMemoryToolsTests
{
    [Fact]
    public void V1Tools_StillRegistered()
    {
        var names = ToolNames();
        Assert.Contains("list_windows", names);
        Assert.Contains("screenshot_window", names);
        Assert.Contains("operate_window", names);
    }

    [Fact]
    public void NewTools_AreRegistered()
    {
        var names = ToolNames();
        Assert.Contains("observe_window", names);
        Assert.Contains("remember_screen", names);
        Assert.Contains("remember_control", names);
        Assert.Contains("click_control", names);
        Assert.Contains("list_remembered", names);
        Assert.Contains("forget_controls", names);
    }

    [Fact]
    public void OperateWindow_Description_MentionsVisualizedFrame()
    {
        var method = typeof(ComputerUseTools).GetMethod(nameof(ComputerUseTools.OperateWindow));
        Assert.NotNull(method);
        var desc = method!.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";
        Assert.True(
            desc.Contains("visualized", StringComparison.OrdinalIgnoreCase)
            || desc.Contains("frame_not_visualized", StringComparison.OrdinalIgnoreCase)
            || desc.Contains("screenshot", StringComparison.OrdinalIgnoreCase),
            "operate_window description should mention visualized/screenshot frames for pointer actions.");
    }

    private static HashSet<string> ToolNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in typeof(ComputerUseTools).GetMethods())
        {
            var attr = method.GetCustomAttribute<McpServerToolAttribute>();
            if (attr?.Name is { Length: > 0 } name)
                names.Add(name);
        }

        return names;
    }
}

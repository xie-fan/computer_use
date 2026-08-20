using System.ComponentModel;
using System.Text.Json;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ComputerUse.Mcp.Mcp;

[McpServerToolType]
internal sealed class ComputerUseTools
{
    private readonly WindowListService _list;
    private readonly ScreenshotService _screenshot;
    private readonly OperateService _operate;
    private readonly Limits _limits;
    private readonly ILogger<ComputerUseTools> _logger;

    public ComputerUseTools(
        WindowListService list,
        ScreenshotService screenshot,
        OperateService operate,
        Limits limits,
        ILogger<ComputerUseTools> logger)
    {
        _list = list;
        _screenshot = screenshot;
        _operate = operate;
        _limits = limits;
        _logger = logger;
    }

    [McpServerTool(Name = "list_windows", Title = "List windows", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("List top-level windows on the current Windows session. Returns opaque targetToken values; do not treat hwnd as identity.")]
    public CallToolResult ListWindows()
    {
        try
        {
            return ToolResults.Ok(_list.List());
        }
        catch (ComputerUseException ex)
        {
            return ToolResults.Error(ex);
        }
        catch (Exception ex)
        {
            return ToolResults.Unexpected(ex, _logger, "list_windows");
        }
    }

    [McpServerTool(Name = "screenshot_window", Title = "Screenshot window", ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Capture one window identified by targetToken. Coordinates in the returned image are only valid with the returned frameId.")]
    public async Task<CallToolResult> ScreenshotWindow(
        [Description("Opaque target token from list_windows.")] string targetToken,
        CancellationToken cancellationToken)
    {
        try
        {
            var (json, png) = await _screenshot.CaptureAsync(targetToken, cancellationToken).ConfigureAwait(false);
            return ToolResults.Ok(json, png);
        }
        catch (ComputerUseException ex)
        {
            return ToolResults.Error(ex);
        }
        catch (Exception ex)
        {
            return ToolResults.Unexpected(ex, _logger, "screenshot_window");
        }
    }

    [McpServerTool(Name = "operate_window", Title = "Operate window", ReadOnly = false, Destructive = true, OpenWorld = false, Idempotent = false)]
    [Description("Send pointer, key, text, or paste actions to a window. frameId is required. Pointer coordinates are relative to that frame.")]
    public async Task<CallToolResult> OperateWindow(
        [Description("Opaque target token from list_windows.")] string targetToken,
        [Description("Frame id from screenshot_window. Required even for key/text/paste/wait.")] string frameId,
        [Description("Array of 1–32 action objects.")] JsonElement actions,
        [Description("Pause in ms between actions (not after the last). Default 100, max 1000.")] int? pauseMs = null,
        [Description("Optional dedupe key. Do not replay when outcomeKnown is false.")] string? operationId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var parsed = ActionPrevalidator.Parse(targetToken, frameId, actions, pauseMs, operationId, _limits);
            var json = await _operate.ExecuteAsync(parsed, cancellationToken).ConfigureAwait(false);
            return ToolResults.Ok(json);
        }
        catch (ComputerUseException ex)
        {
            return ToolResults.Error(ex);
        }
        catch (Exception ex)
        {
            return ToolResults.Unexpected(ex, _logger, "operate_window");
        }
    }
}

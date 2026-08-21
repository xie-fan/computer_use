using System.ComponentModel;
using System.Text.Json;
using ComputerUse.Mcp.Abstractions;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Identity;
using ComputerUse.Mcp.Memory;
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
    private readonly ObserveService _observe;
    private readonly RememberService _remember;
    private readonly ClickControlService _click;
    private readonly FrameCache _frames;
    private readonly MemoryCatalog _catalog;
    private readonly TargetTokenService _tokens;
    private readonly IWindowQuery _windows;
    private readonly IProcessQuery _processes;
    private readonly IHostProcessResolver _host;
    private readonly AppIdentityFactory _identities;
    private readonly Limits _limits;
    private readonly ILogger<ComputerUseTools> _logger;

    public ComputerUseTools(
        WindowListService list,
        ScreenshotService screenshot,
        OperateService operate,
        ObserveService observe,
        RememberService remember,
        ClickControlService click,
        FrameCache frames,
        MemoryCatalog catalog,
        TargetTokenService tokens,
        IWindowQuery windows,
        IProcessQuery processes,
        IHostProcessResolver host,
        AppIdentityFactory identities,
        Limits limits,
        ILogger<ComputerUseTools> logger)
    {
        _list = list;
        _screenshot = screenshot;
        _operate = operate;
        _observe = observe;
        _remember = remember;
        _click = click;
        _frames = frames;
        _catalog = catalog;
        _tokens = tokens;
        _windows = windows;
        _processes = processes;
        _host = host;
        _identities = identities;
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
    [Description("Send pointer, key, text, or paste actions to a window. frameId is required. Pointer coordinates are relative to that frame. Pointer actions may only use a visualized frameId returned by screenshot_window; an observe_window frame with visualized:false is rejected as frame_not_visualized. Key/text/paste/wait may use an observe frameId only to confirm the same window epoch.")]
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

    [McpServerTool(Name = "observe_window", Title = "Observe window", ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Capture a window for control-memory observe. Returns frameId with visualized:false and no PNG. Do not use this frameId for operate_window pointer actions (that yields frame_not_visualized) or remember_*; use screenshot_window when the model must see pixels. HostWindow returns empty controls.")]
    public async Task<CallToolResult> ObserveWindow(
        [Description("Opaque target token from list_windows.")] string targetToken,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _observe.ObserveAsync(targetToken, cancellationToken).ConfigureAwait(false);
            return ToolResults.Ok(result);
        }
        catch (ComputerUseException ex)
        {
            return ToolResults.Error(ex);
        }
        catch (Exception ex)
        {
            return ToolResults.Unexpected(ex, _logger, "observe_window");
        }
    }

    [McpServerTool(Name = "remember_screen", Title = "Remember screen", ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Archive screen fingerprints from a visualized screenshot_window frame. fingerprints are half-open integer pixel boxes {x,y,width,height} relative to that frame. Observe frames with visualized:false are rejected. HostWindow is forbidden.")]
    public CallToolResult RememberScreen(
        [Description("Opaque target token from list_windows.")] string targetToken,
        [Description("Visualized frameId from screenshot_window.")] string frameId,
        [Description("Display label for this screen.")] string screenKey,
        [Description("Half-open integer pixel boxes {x,y,width,height} relative to the frame. Default two spatially spread boxes.")] JsonElement fingerprints)
    {
        try
        {
            var (token, appKey, hostWindow, diagnostics) = ResolveTarget(targetToken);
            var frame = RequireVisualizedFrame(frameId, token);
            var boxes = ParseBoxes(fingerprints, "fingerprints");
            var screenId = _remember.RememberScreen(frame, appKey, screenKey, boxes, hostWindow, diagnostics);
            return ToolResults.Ok(new RememberScreenResult { ScreenId = screenId });
        }
        catch (ComputerUseException ex)
        {
            return ToolResults.Error(ex);
        }
        catch (Exception ex)
        {
            return ToolResults.Unexpected(ex, _logger, "remember_screen");
        }
    }

    [McpServerTool(Name = "remember_control", Title = "Remember control", ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Archive a control template from a visualized screenshot_window frame. box is a half-open integer pixel rectangle {x,y,width,height} relative to that frame. Observe frames with visualized:false are rejected. HostWindow is forbidden.")]
    public CallToolResult RememberControl(
        [Description("Opaque target token from list_windows.")] string targetToken,
        [Description("Visualized frameId from screenshot_window.")] string frameId,
        [Description("Screen id that owns this control.")] string screenId,
        [Description("Display name for this control.")] string name,
        [Description("Half-open integer pixel box {x,y,width,height} relative to the frame.")] JsonElement box)
    {
        try
        {
            var (token, appKey, hostWindow, _) = ResolveTarget(targetToken);
            var frame = RequireVisualizedFrame(frameId, token);
            var parsed = ParseBox(box, "box");
            var controlId = _remember.RememberControl(frame, appKey, screenId, name, parsed, hostWindow);
            return ToolResults.Ok(new RememberControlResult { ControlId = controlId });
        }
        catch (ComputerUseException ex)
        {
            return ToolResults.Error(ex);
        }
        catch (Exception ex)
        {
            return ToolResults.Unexpected(ex, _logger, "remember_control");
        }
    }

    [McpServerTool(Name = "click_control", Title = "Click control", ReadOnly = false, Destructive = true, OpenWorld = false, Idempotent = false)]
    [Description("Click a remembered control by controlId after re-identifying the current screen. Optional operationId uses the same dedupe semantics as operate_window. HostWindow is forbidden.")]
    public async Task<CallToolResult> ClickControl(
        [Description("Opaque target token from list_windows.")] string targetToken,
        [Description("Control id from remember_control or observe_window.")] string controlId,
        [Description("Optional dedupe key. Do not replay when outcomeKnown is false.")] string? operationId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _click.ClickAsync(
                targetToken,
                controlId,
                string.IsNullOrWhiteSpace(operationId) ? null : operationId,
                cancellationToken).ConfigureAwait(false);
            return ToolResults.Ok(result);
        }
        catch (ComputerUseException ex)
        {
            return ToolResults.Error(ex);
        }
        catch (Exception ex)
        {
            return ToolResults.Unexpected(ex, _logger, "click_control");
        }
    }

    [McpServerTool(Name = "list_remembered", Title = "List remembered", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("List remembered screens and controls for this window's AppKey. No images. HostWindow returns empty lists with hostWindow:true (not an error).")]
    public CallToolResult ListRemembered(
        [Description("Opaque target token from list_windows.")] string targetToken)
    {
        try
        {
            var (_, appKey, hostWindow, _) = ResolveTarget(targetToken);
            if (hostWindow)
            {
                return ToolResults.Ok(new ListRememberedResult
                {
                    HostWindow = true,
                    Screens = [],
                    Controls = []
                });
            }

            var screens = _catalog.List(appKey);
            var controls = FlattenControls(screens);
            return ToolResults.Ok(new ListRememberedResult
            {
                HostWindow = false,
                Screens = screens,
                Controls = controls
            });
        }
        catch (ComputerUseException ex)
        {
            return ToolResults.Error(ex);
        }
        catch (Exception ex)
        {
            return ToolResults.Unexpected(ex, _logger, "list_remembered");
        }
    }

    [McpServerTool(Name = "forget_controls", Title = "Forget controls", ReadOnly = false, Destructive = true, OpenWorld = false)]
    [Description("Delete remembered screen/control crops from disk. targetToken is required to resolve the AppKey; optional screenId and/or controlId select what to delete. HostWindow is a no-op (not an error).")]
    public CallToolResult ForgetControls(
        [Description("Required opaque target token from list_windows. Used to resolve the AppKey.")] string? targetToken = null,
        [Description("Optional screen id to delete (including its controls).")] string? screenId = null,
        [Description("Optional control id to delete.")] string? controlId = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(targetToken))
            {
                throw new ComputerUseException(
                    ErrorCodes.InvalidAction,
                    "forget_controls requires targetToken to resolve the AppKey.");
            }

            var (_, appKey, hostWindow, _) = ResolveTarget(targetToken);
            if (hostWindow)
            {
                return ToolResults.Ok(new ForgetControlsResult { HostWindow = true });
            }

            if (!string.IsNullOrWhiteSpace(controlId))
                _catalog.ForgetControl(appKey, controlId);
            if (!string.IsNullOrWhiteSpace(screenId))
                _catalog.ForgetScreen(appKey, screenId);

            return ToolResults.Ok(new ForgetControlsResult { HostWindow = false });
        }
        catch (ComputerUseException ex)
        {
            return ToolResults.Error(ex);
        }
        catch (Exception ex)
        {
            return ToolResults.Unexpected(ex, _logger, "forget_controls");
        }
    }

    private (TargetTokenPayload Token, string AppKey, bool HostWindow, AppIdentity? Diagnostics) ResolveTarget(string targetToken)
    {
        var token = _tokens.RequireValid(targetToken, _windows, _processes);
        var hostWindow = _host.IsHostProcess(token.Pid, token.CreateTimeUtc);
        if (hostWindow)
            return (token, "", true, null);

        var app = _identities.Resolve(token.Pid, token.CreateTimeUtc, token.ClassName);
        return (token, app.Value, false, app.Diagnostics);
    }

    private FrameRecord RequireVisualizedFrame(string frameId, TargetTokenPayload token)
    {
        var frame = _frames.Require(frameId);
        _frames.EnsureMatchesToken(frame, token);
        return frame;
    }

    private static IReadOnlyList<RememberedControl> FlattenControls(IReadOnlyList<RememberedScreen> screens)
    {
        if (screens.Count == 0)
            return [];

        var controls = new List<RememberedControl>();
        foreach (var screen in screens)
            controls.AddRange(screen.Controls);
        return controls;
    }

    private static IReadOnlyList<PixelBox> ParseBoxes(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw new ComputerUseException(ErrorCodes.InvalidAction, $"{name} must be a JSON array of {{x,y,width,height}}.");

        var boxes = new List<PixelBox>(element.GetArrayLength());
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            try
            {
                boxes.Add(ParseBox(item, name));
            }
            catch (ComputerUseException ex)
            {
                throw new ComputerUseException(ex.Code, $"{name}[{index}]: {ex.Message}");
            }

            index++;
        }

        return boxes;
    }

    private static PixelBox ParseBox(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new ComputerUseException(ErrorCodes.InvalidAction, $"{name} must be an object {{x,y,width,height}}.");

        return new PixelBox(
            RequireInt(element, "x"),
            RequireInt(element, "y"),
            RequireInt(element, "width"),
            RequireInt(element, "height"));
    }

    private static int RequireInt(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el))
            throw new ComputerUseException(ErrorCodes.InvalidAction, $"{name} is required.");
        if (el.ValueKind != JsonValueKind.Number || !el.TryGetInt32(out var value))
            throw new ComputerUseException(ErrorCodes.InvalidAction, $"{name} must be a finite integer.");
        return value;
    }
}

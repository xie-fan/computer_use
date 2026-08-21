using System.Text.Json;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace ComputerUse.Mcp.Mcp;

internal static class ToolResults
{
    public static JsonSerializerOptions Json => EnvelopeJson.Options;

    public static JsonElement SerializeStructured(object payload) => payload switch
    {
        OperateOutcome operate => JsonSerializer.SerializeToElement(operate, ComputerUseJsonContext.Default.OperateOutcome),
        ScreenshotResult shot => JsonSerializer.SerializeToElement(shot, ComputerUseJsonContext.Default.ScreenshotResult),
        ObserveResult observe => JsonSerializer.SerializeToElement(observe, ComputerUseJsonContext.Default.ObserveResult),
        RememberScreenResult rememberScreen => JsonSerializer.SerializeToElement(rememberScreen, ComputerUseJsonContext.Default.RememberScreenResult),
        RememberControlResult rememberControl => JsonSerializer.SerializeToElement(rememberControl, ComputerUseJsonContext.Default.RememberControlResult),
        ListRememberedResult listed => JsonSerializer.SerializeToElement(listed, ComputerUseJsonContext.Default.ListRememberedResult),
        ForgetControlsResult forgotten => JsonSerializer.SerializeToElement(forgotten, ComputerUseJsonContext.Default.ForgetControlsResult),
        ClickControlResult clicked => JsonSerializer.SerializeToElement(clicked, ComputerUseJsonContext.Default.ClickControlResult),
        _ => JsonSerializer.SerializeToElement(payload, Json)
    };

    public static JsonElement SerializeError(ComputerUseException ex)
    {
        var envelope = new ErrorEnvelope
        {
            Code = ex.Code,
            Message = ex.Message,
            Details = EnvelopeJson.Details(ex.Details)
        };
        return JsonSerializer.SerializeToElement(envelope, ComputerUseJsonContext.Default.ErrorEnvelope);
    }

    public static CallToolResult Ok(object payload, byte[]? png = null)
    {
        var structured = SerializeStructured(payload);
        var text = structured.GetRawText();
        var content = new List<ContentBlock>();
        if (png is not null)
            content.Add(ImageContentBlock.FromBytes(png, "image/png"));
        content.Add(new TextContentBlock { Text = text });
        return new CallToolResult
        {
            Content = content,
            StructuredContent = structured,
            IsError = false
        };
    }

    public static CallToolResult Error(ComputerUseException ex)
    {
        var structured = SerializeError(ex);
        var text = structured.GetRawText();
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = text }],
            StructuredContent = structured,
            IsError = true
        };
    }

    public static CallToolResult Unexpected(Exception ex, ILogger logger, string tool)
    {
        logger.LogError(ex, "tool={Tool} unhandled", tool);
        return Error(new ComputerUseException(ErrorCodes.ActionFailed, "Internal error."));
    }
}

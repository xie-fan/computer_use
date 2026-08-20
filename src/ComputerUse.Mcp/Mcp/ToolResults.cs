using System.Text.Json;
using System.Text.Json.Serialization;
using ComputerUse.Mcp.Domain;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace ComputerUse.Mcp.Mcp;

internal static class ToolResults
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public static CallToolResult Ok(object payload, byte[]? png = null)
    {
        var text = JsonSerializer.Serialize(payload, Json);
        var structured = JsonSerializer.SerializeToElement(payload, Json);
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
        var envelope = new ErrorEnvelope
        {
            Code = ex.Code,
            Message = ex.Message,
            Details = ex.Details
        };
        var text = JsonSerializer.Serialize(envelope, Json);
        var structured = JsonSerializer.SerializeToElement(envelope, Json);
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

using System.Text.Json.Serialization;
using ComputerUse.Mcp.Domain;

namespace ComputerUse.Mcp.Mcp;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(OperateOutcome))]
[JsonSerializable(typeof(ScreenshotResult))]
[JsonSerializable(typeof(ErrorEnvelope))]
[JsonSerializable(typeof(WarningItem))]
[JsonSerializable(typeof(SideEffects))]
[JsonSerializable(typeof(TransformDto))]
[JsonSerializable(typeof(RectDto))]
[JsonSerializable(typeof(PointDto))]
[JsonSerializable(typeof(SizeDto))]
[JsonSerializable(typeof(DpiDto))]
[JsonSerializable(typeof(MonitorRefDto))]
internal partial class ComputerUseJsonContext : JsonSerializerContext;

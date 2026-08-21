using System.Text.Json.Serialization;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Memory;
using ComputerUse.Mcp.Services;

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
[JsonSerializable(typeof(ObserveResult))]
[JsonSerializable(typeof(RememberedControl))]
[JsonSerializable(typeof(RememberedScreen))]
[JsonSerializable(typeof(RememberScreenResult))]
[JsonSerializable(typeof(RememberControlResult))]
[JsonSerializable(typeof(ListRememberedResult))]
[JsonSerializable(typeof(ForgetControlsResult))]
[JsonSerializable(typeof(ClickControlResult))]
[JsonSerializable(typeof(ClickControlMatch))]
internal partial class ComputerUseJsonContext : JsonSerializerContext;

internal sealed class RememberScreenResult
{
    public required string ScreenId { get; init; }
}

internal sealed class RememberControlResult
{
    public required string ControlId { get; init; }
}

internal sealed class ListRememberedResult
{
    public bool HostWindow { get; init; }
    public IReadOnlyList<RememberedScreen> Screens { get; init; } = [];
    public IReadOnlyList<RememberedControl> Controls { get; init; } = [];
}

internal sealed class ForgetControlsResult
{
    public bool HostWindow { get; init; }
}

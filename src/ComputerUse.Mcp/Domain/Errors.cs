using System.Text.Json;

namespace ComputerUse.Mcp.Domain;

internal static class ErrorCodes
{
    public const string StaleTarget = "stale_target";
    public const string StaleCapture = "stale_capture";
    public const string WindowNotFound = "window_not_found";
    public const string HostWindowForbidden = "host_window_forbidden";
    public const string SecureDesktopForbidden = "secure_desktop_forbidden";
    public const string SessionNotInteractive = "session_not_interactive";
    public const string IntegrityLevelBlocked = "integrity_level_blocked";
    public const string OffCurrentDesktop = "off_current_desktop";
    public const string DesktopStateUnknown = "desktop_state_unknown";
    public const string ActivationFailed = "activation_failed";
    public const string FocusLost = "focus_lost";
    public const string PointOccluded = "point_occluded";
    public const string PointOffscreen = "point_offscreen";
    public const string InputPositionMismatch = "input_position_mismatch";
    public const string CaptureFailed = "capture_failed";
    public const string CaptureTimeout = "capture_timeout";
    public const string CaptureUnsupported = "capture_unsupported";
    public const string EmptyFrame = "empty_frame";
    public const string ProtectedContent = "protected_content";
    public const string ActionFailed = "action_failed";
    public const string InvalidAction = "invalid_action";
    public const string TooManyActions = "too_many_actions";
    public const string PayloadTooLarge = "payload_too_large";
    public const string Busy = "busy";
    public const string Timeout = "timeout";
    public const string Cancelled = "cancelled";
    public const string DuplicateInFlight = "duplicate_in_flight";
    public const string ClipboardFailed = "clipboard_failed";

    public const string FrameNotVisualized = "frame_not_visualized";
    public const string ScreenUnknown = "screen_unknown";
    public const string ScreenAmbiguous = "screen_ambiguous";
    public const string ScreenMismatch = "screen_mismatch";
    public const string TemplateNotFound = "template_not_found";
    public const string TemplateAmbiguous = "template_ambiguous";
    public const string TemplateScaleMismatch = "template_scale_mismatch";
    public const string UnknownControl = "unknown_control";
    public const string LowEntropyCrop = "low_entropy_crop";
    public const string AppIdentityUnavailable = "app_identity_unavailable";
}

internal sealed class ComputerUseException : Exception
{
    public ComputerUseException(string code, string message, object? details = null)
        : base(message)
    {
        Code = code;
        Details = details;
    }

    public string Code { get; }
    public object? Details { get; }

    public ComputerUseException WithDetails(object details) =>
        new(Code, Message, details);
}

internal sealed class ErrorEnvelope
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public JsonElement? Details { get; init; }
}

internal sealed class WarningItem
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public JsonElement? Details { get; init; }
}

internal sealed class SideEffects
{
    public bool WindowRestored { get; set; }
    public bool ForegroundChanged { get; set; }
    public bool DesktopChanged { get; set; }
    public bool FinalStateKnown { get; set; } = true;
}

internal sealed class OperateOutcome
{
    public int CompletedCount { get; init; }
    public int? FailedIndex { get; init; }
    public bool OutcomeKnown { get; init; } = true;
    public bool MayHaveExecuted { get; init; }
    public string? Code { get; init; }
    public IReadOnlyList<WarningItem> Warnings { get; init; } = [];
    public SideEffects SideEffects { get; init; } = new();
    public string ContractVersion { get; init; } = Contract.Version;
    public string ServerVersion { get; init; } = Contract.ServerVersion;
}

internal sealed class ScreenshotResult
{
    public required string FrameId { get; init; }
    public required string TargetToken { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int SourceWidth { get; init; }
    public int SourceHeight { get; init; }
    public double Scale { get; init; }
    public required string CaptureMethod { get; init; }
    public required TransformDto Transform { get; init; }
    public required DpiDto Dpi { get; init; }
    public required RectDto Bounds { get; init; }
    public required MonitorRefDto Monitor { get; init; }
    public DateTimeOffset CapturedAt { get; init; }
    public required SideEffects SideEffects { get; init; }
    public string ContractVersion { get; init; } = Contract.Version;
    public string ServerVersion { get; init; } = Contract.ServerVersion;
}

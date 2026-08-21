namespace ComputerUse.Mcp.Domain;

internal sealed record Limits
{
    public int MaxActionsPerRequest { get; init; } = 32;
    public int MaxTextUtf16 { get; init; } = 8192;
    public int MaxPauseMs { get; init; } = 1000;
    public int MaxWaitMs { get; init; } = 5000;
    public int RequestDeadlineMs { get; init; } = 15_000;
    public int CaptureTimeoutMs { get; init; } = 5_000;
    public int MaxReturnedLongEdge { get; init; } = 1280;
    public int MaxPngBytes { get; init; } = 4_000_000;
    public int MaxListWindows { get; init; } = 256;
    public int MaxQueuedOperations { get; init; } = 4;
    public int FrameTtlMs { get; init; } = 120_000;
    public int MaxCachedFrames { get; init; } = 8;
    public int OperationIdTtlMs { get; init; } = 60_000;

    public int DefaultPauseMs { get; init; } = 100;
    public int DefaultStepTimeoutMs { get; init; } = 3_000;
    public int RestoreTimeoutMs { get; init; } = 2_000;
    public int ClipboardRestoreWaitMs { get; init; } = 300;
    public int GeometryEpsilonPx { get; init; } = 1;
    public int InputPositionEpsilonPx { get; init; } = 3;
    public int MaxOperationIdChars { get; init; } = 128;

    public int MaxScreensPerAppKey { get; init; } = 32;
    public int MaxControlsPerScreen { get; init; } = 64;
    public int MaxTemplateLongEdge { get; init; } = 256;
    /// <summary>ZNCC 搜索硬超时，远小于 RequestDeadlineMs，避免小模板全帧回退饿死 Coordinator。</summary>
    public int ZnccSearchTimeoutMs { get; init; } = 1_500;
    public int MaxMemoryLibraryBytes { get; init; } = 256 * 1024 * 1024;
    public int MemorySoftTtlDays { get; init; } = 30;
    public int MinCropEdgePx { get; init; } = 24;
    public double TemplateScaleMin { get; init; } = 0.85;
    public double TemplateScaleMax { get; init; } = 1.15;

    public static Limits V1 { get; } = new();

    public object ToPublicDto() => new
    {
        maxActionsPerRequest = MaxActionsPerRequest,
        maxTextUtf16 = MaxTextUtf16,
        maxPauseMs = MaxPauseMs,
        maxWaitMs = MaxWaitMs,
        requestDeadlineMs = RequestDeadlineMs,
        captureTimeoutMs = CaptureTimeoutMs,
        maxReturnedLongEdge = MaxReturnedLongEdge,
        maxPngBytes = MaxPngBytes,
        maxListWindows = MaxListWindows,
        maxQueuedOperations = MaxQueuedOperations,
        frameTtlMs = FrameTtlMs,
        maxCachedFrames = MaxCachedFrames,
        operationIdTtlMs = OperationIdTtlMs
    };
}

internal static class Contract
{
    public const string Version = "1";
    public const string ServerVersion = "1.0.0";
    public const string ServerName = "computer_use";
}

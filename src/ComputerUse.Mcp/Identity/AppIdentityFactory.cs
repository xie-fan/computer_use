using ComputerUse.Mcp.Abstractions;
using ComputerUse.Mcp.Domain;

namespace ComputerUse.Mcp.Identity;

internal sealed class AppIdentityFactory
{
    private const int MaxCacheEntries = 256;

    private readonly IProcessQuery _processes;
    private readonly object _gate = new();
    private readonly Dictionary<(uint Pid, long CreateTimeUtc), ProcessFields> _cache = [];

    public AppIdentityFactory(IProcessQuery processes)
    {
        ArgumentNullException.ThrowIfNull(processes);
        _processes = processes;
    }

    public AppKey Resolve(uint pid, long createTimeUtc, string className)
    {
        ArgumentNullException.ThrowIfNull(className);

        var fields = GetOrQueryFields(pid, createTimeUtc);
        var keyPath = AppKeyImagePath.Normalize(fields.ImagePath);
        var identity = new AppIdentity(
            Blank(fields.PackageFamilyName),
            Blank(fields.SignerSubject),
            Blank(fields.ProductName),
            Blank(fields.ProductVersion),
            keyPath,
            className)
        {
            RawImagePath = fields.ImagePath
        };

        if (!AppKeyResolver.HasStableIdentity(identity))
        {
            throw new ComputerUseException(
                ErrorCodes.AppIdentityUnavailable,
                "The window process identity could not be resolved. screenshot_window still works; remember/click/observe require a stable AppKey.");
        }

        return AppKeyResolver.Compute(identity);
    }

    private ProcessFields GetOrQueryFields(uint pid, long createTimeUtc)
    {
        var key = (pid, createTimeUtc);
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var cached))
                return cached;
        }

        // 一次取齐，避免每次 observe/click 都验签。
        var fields = new ProcessFields(
            Blank(_processes.TryGetPackageFamilyName(pid)),
            Blank(_processes.TryGetSignerSubject(pid)),
            Blank(_processes.TryGetProductName(pid)),
            Blank(_processes.TryGetProductVersion(pid)),
            _processes.TryGetNormalizedImagePath(pid));

        lock (_gate)
        {
            if (_cache.Count >= MaxCacheEntries)
                _cache.Clear();
            _cache[key] = fields;
            return fields;
        }
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private readonly record struct ProcessFields(
        string? PackageFamilyName,
        string? SignerSubject,
        string? ProductName,
        string? ProductVersion,
        string? ImagePath);
}

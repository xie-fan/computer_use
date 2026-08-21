using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Identity;

namespace ComputerUse.Mcp.Memory;

internal sealed record RememberedControl(string ControlId, string Name, DateTimeOffset? LastMatchedAt = null);

internal sealed record RememberedScreen(
    string ScreenId,
    string ScreenKey,
    int FingerprintCount,
    IReadOnlyList<RememberedControl> Controls,
    DateTimeOffset? LastMatchedAt = null);

internal sealed record CatalogFingerprint(
    int X,
    int Y,
    int Width,
    int Height,
    double Nx,
    double Ny,
    double Nw,
    double Nh,
    byte[] Bgra);

internal sealed record CatalogControl(
    string ControlId,
    string Name,
    string ScreenId,
    double Nx,
    double Ny,
    double Nw,
    double Nh,
    int Width,
    int Height,
    byte[]? Bgra,
    int SourceWidth,
    int SourceHeight,
    uint DpiX,
    uint DpiY);

internal sealed record FingerprintAsset(
    int X,
    int Y,
    int Width,
    int Height,
    byte[] Png,
    double Nx,
    double Ny,
    double Nw,
    double Nh);

internal sealed record ControlAsset(
    byte[] Png,
    int Width,
    int Height,
    double Nx,
    double Ny,
    double Nw,
    double Nh,
    int SourceWidth,
    int SourceHeight,
    uint DpiX,
    uint DpiY);

internal sealed record ScreenSnapshot(
    int Width,
    int Height,
    int SourceWidth,
    int SourceHeight,
    uint DpiX,
    uint DpiY,
    ulong PhashBits);

internal sealed record CatalogScreenAssets(
    string ScreenId,
    string ScreenKey,
    ulong PhashBits,
    IReadOnlyList<CatalogFingerprint> Fingerprints,
    IReadOnlyList<CatalogControl> Controls);

internal sealed record AppCatalogMetadata(
    string AppKey,
    string? PackageFamilyName,
    string? SignerSubject,
    string? ProductName,
    string? ProductVersion,
    string? ImagePath,
    string? ClassName);

internal sealed class MemoryCatalog
{
    private const string ScreensFolder = "screens";
    private const string ControlsFolder = "controls";
    private const string FingerprintsFolder = "fingerprints";
    private const string AppFileName = "app.json";
    private const string ScreenFileName = "screen.json";
    private const string LockFileName = "memory.lock";
    private static readonly TimeSpan StoreLockTimeout = TimeSpan.FromSeconds(5);

    private readonly string _root;
    private readonly Limits _limits;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly object _gate = new();
    private long _usedBytes;
    private bool _usedBytesTrusted;

    public static string DefaultRootDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "computer-use-mcp",
            "memory");

    public MemoryCatalog(string rootDirectory, Limits limits, Func<DateTimeOffset>? utcNow = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(limits);
        _root = Path.GetFullPath(rootDirectory);
        _limits = limits;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        var created = !Directory.Exists(_root);
        Directory.CreateDirectory(_root);
        if (created)
            TryRestrictDirectoryAcl(_root);
        RecalibrateUsedBytes();
    }

    public string PutScreen(string appKey, string screenKey, int fingerprintCount, AppIdentity? diagnostics = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(screenKey);
        ArgumentOutOfRangeException.ThrowIfNegative(fingerprintCount);

        return WithStoreLock(() =>
        {
            var appDir = EnsureAppDirectory(appKey, diagnostics);
            PrepareWriteUnlocked(protectScreenDir: null, screenCountAppDir: appDir, additionalBytes: 0);
            EnsureLibraryQuota();
            var screenCount = CountScreens(appDir);
            if (screenCount >= _limits.MaxScreensPerAppKey)
            {
                throw new ComputerUseException(
                    ErrorCodes.PayloadTooLarge,
                    $"This AppKey already has {_limits.MaxScreensPerAppKey} screens.",
                    new { maxScreensPerAppKey = _limits.MaxScreensPerAppKey });
            }

            var screenId = "sc1." + Guid.NewGuid().ToString("N");
            if (!TryResolveScreenDirectory(appDir, screenId, out var screenDir))
            {
                throw new ComputerUseException(
                    ErrorCodes.UnknownControl,
                    "The issued screenId is not contained in the screens directory.");
            }

            Directory.CreateDirectory(screenDir);
            var jsonPath = Path.Combine(screenDir, ScreenFileName);
            WriteJsonAccounted(
                jsonPath,
                new StoredScreen
                {
                    ScreenId = screenId,
                    ScreenKey = screenKey,
                    FingerprintCount = fingerprintCount,
                    CreatedAt = _utcNow()
                });
            return screenId;
        });
    }

    public string PutScreen(
        string appKey,
        string screenKey,
        IReadOnlyList<FingerprintAsset> fingerprints,
        ScreenSnapshot snapshot,
        AppIdentity? diagnostics = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(screenKey);
        ArgumentNullException.ThrowIfNull(fingerprints);
        ArgumentNullException.ThrowIfNull(snapshot);

        return WithStoreLock(() =>
        {
            var extraBytes = SumPngBytes(fingerprints);
            var appDir = EnsureAppDirectory(appKey, diagnostics);
            PrepareWriteUnlocked(protectScreenDir: null, screenCountAppDir: appDir, additionalBytes: extraBytes);
            EnsureLibraryQuota(extraBytes);
            var screenCount = CountScreens(appDir);
            if (screenCount >= _limits.MaxScreensPerAppKey)
            {
                throw new ComputerUseException(
                    ErrorCodes.PayloadTooLarge,
                    $"This AppKey already has {_limits.MaxScreensPerAppKey} screens.",
                    new { maxScreensPerAppKey = _limits.MaxScreensPerAppKey });
            }

            var screenId = "sc1." + Guid.NewGuid().ToString("N");
            if (!TryResolveScreenDirectory(appDir, screenId, out var screenDir))
            {
                throw new ComputerUseException(
                    ErrorCodes.UnknownControl,
                    "The issued screenId is not contained in the screens directory.");
            }

            Directory.CreateDirectory(screenDir);
            var fingerprintDir = Path.Combine(screenDir, FingerprintsFolder);
            Directory.CreateDirectory(fingerprintDir);

            var metas = new List<StoredFingerprintMeta>(fingerprints.Count);
            for (var i = 0; i < fingerprints.Count; i++)
            {
                var asset = fingerprints[i];
                ArgumentNullException.ThrowIfNull(asset.Png);
                var fileName = i + ".png";
                var pngPath = Path.Combine(fingerprintDir, fileName);
                WriteBytesAccounted(pngPath, asset.Png);
                metas.Add(new StoredFingerprintMeta
                {
                    Index = i,
                    X = asset.X,
                    Y = asset.Y,
                    Width = asset.Width,
                    Height = asset.Height,
                    Nx = asset.Nx,
                    Ny = asset.Ny,
                    Nw = asset.Nw,
                    Nh = asset.Nh,
                    File = fileName
                });
            }

            var screenJson = Path.Combine(screenDir, ScreenFileName);
            WriteJsonAccounted(
                screenJson,
                new StoredScreen
                {
                    ScreenId = screenId,
                    ScreenKey = screenKey,
                    FingerprintCount = fingerprints.Count,
                    CreatedAt = _utcNow(),
                    Width = snapshot.Width,
                    Height = snapshot.Height,
                    SourceWidth = snapshot.SourceWidth,
                    SourceHeight = snapshot.SourceHeight,
                    DpiX = snapshot.DpiX,
                    DpiY = snapshot.DpiY,
                    PhashHex = snapshot.PhashBits.ToString("X16"),
                    Fingerprints = metas
                });
            return screenId;
        });
    }

    public string PutControl(string appKey, string screenId, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(screenId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return WithStoreLock(() =>
        {
            if (!TryResolveScreenDirectory(GetAppDirectory(appKey), screenId, out var screenDir)
                || !File.Exists(Path.Combine(screenDir, ScreenFileName)))
            {
                throw new ComputerUseException(
                    ErrorCodes.UnknownControl,
                    "The screenId is unknown for this AppKey.");
            }

            PrepareWriteUnlocked(protectScreenDir: screenDir, screenCountAppDir: null, additionalBytes: 0);
            if (!File.Exists(Path.Combine(screenDir, ScreenFileName)))
            {
                throw new ComputerUseException(
                    ErrorCodes.UnknownControl,
                    "The screenId is unknown for this AppKey.");
            }

            if (TryFindControlByName(screenDir, name, out var existing, out var existingJson))
            {
                existing.Name = name;
                WriteJsonAccounted(existingJson, existing);
                return existing.ControlId;
            }

            EnsureLibraryQuota();
            var controlCount = CountControls(screenDir);
            if (controlCount >= _limits.MaxControlsPerScreen)
            {
                throw new ComputerUseException(
                    ErrorCodes.PayloadTooLarge,
                    $"This Screen already has {_limits.MaxControlsPerScreen} controls.",
                    new { maxControlsPerScreen = _limits.MaxControlsPerScreen });
            }

            var controlId = "ct1." + Guid.NewGuid().ToString("N");
            var controlsDir = Path.Combine(screenDir, ControlsFolder);
            Directory.CreateDirectory(controlsDir);
            var jsonPath = Path.Combine(controlsDir, controlId + ".json");
            WriteJsonAccounted(
                jsonPath,
                new StoredControl
                {
                    ControlId = controlId,
                    Name = name,
                    CreatedAt = _utcNow()
                });
            return controlId;
        });
    }

    public string PutControl(string appKey, string screenId, string name, ControlAsset asset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(screenId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(asset.Png);

        return WithStoreLock(() =>
        {
            if (!TryResolveScreenDirectory(GetAppDirectory(appKey), screenId, out var screenDir)
                || !File.Exists(Path.Combine(screenDir, ScreenFileName)))
            {
                throw new ComputerUseException(
                    ErrorCodes.UnknownControl,
                    "The screenId is unknown for this AppKey.");
            }

            PrepareWriteUnlocked(protectScreenDir: screenDir, screenCountAppDir: null, additionalBytes: asset.Png.Length);
            if (!File.Exists(Path.Combine(screenDir, ScreenFileName)))
            {
                throw new ComputerUseException(
                    ErrorCodes.UnknownControl,
                    "The screenId is unknown for this AppKey.");
            }

            if (TryFindControlByName(screenDir, name, out var existing, out var existingJson))
            {
                var pngName = string.IsNullOrWhiteSpace(existing.TemplateFile)
                    ? existing.ControlId + ".png"
                    : existing.TemplateFile;
                if (!IsSafeFileName(pngName))
                    pngName = existing.ControlId + ".png";
                var controlsDir = Path.Combine(screenDir, ControlsFolder);
                Directory.CreateDirectory(controlsDir);
                var pngPath = Path.Combine(controlsDir, pngName);
                var extraBytes = Math.Max(0, asset.Png.Length - FileLength(pngPath));
                EnsureLibraryQuota(extraBytes);
                WriteBytesAccounted(pngPath, asset.Png);
                existing.Name = name;
                existing.Width = asset.Width;
                existing.Height = asset.Height;
                existing.Nx = asset.Nx;
                existing.Ny = asset.Ny;
                existing.Nw = asset.Nw;
                existing.Nh = asset.Nh;
                existing.SourceWidth = asset.SourceWidth;
                existing.SourceHeight = asset.SourceHeight;
                existing.DpiX = asset.DpiX;
                existing.DpiY = asset.DpiY;
                existing.TemplateFile = pngName;
                WriteJsonAccounted(existingJson, existing);
                return existing.ControlId;
            }

            EnsureLibraryQuota(asset.Png.Length);
            var controlCount = CountControls(screenDir);
            if (controlCount >= _limits.MaxControlsPerScreen)
            {
                throw new ComputerUseException(
                    ErrorCodes.PayloadTooLarge,
                    $"This Screen already has {_limits.MaxControlsPerScreen} controls.",
                    new { maxControlsPerScreen = _limits.MaxControlsPerScreen });
            }

            var controlId = "ct1." + Guid.NewGuid().ToString("N");
            var newControlsDir = Path.Combine(screenDir, ControlsFolder);
            Directory.CreateDirectory(newControlsDir);
            var newPngName = controlId + ".png";
            var newPngPath = Path.Combine(newControlsDir, newPngName);
            WriteBytesAccounted(newPngPath, asset.Png);
            var jsonPath = Path.Combine(newControlsDir, controlId + ".json");
            WriteJsonAccounted(
                jsonPath,
                new StoredControl
                {
                    ControlId = controlId,
                    Name = name,
                    CreatedAt = _utcNow(),
                    Width = asset.Width,
                    Height = asset.Height,
                    Nx = asset.Nx,
                    Ny = asset.Ny,
                    Nw = asset.Nw,
                    Nh = asset.Nh,
                    SourceWidth = asset.SourceWidth,
                    SourceHeight = asset.SourceHeight,
                    DpiX = asset.DpiX,
                    DpiY = asset.DpiY,
                    TemplateFile = newPngName
                });
            return controlId;
        });
    }

    public bool ScreenExists(string appKey, string screenId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(screenId);
        lock (_gate)
        {
            return TryResolveScreenDirectory(GetAppDirectory(appKey), screenId, out var screenDir)
                && File.Exists(Path.Combine(screenDir, ScreenFileName));
        }
    }

    public IReadOnlyList<CatalogFingerprint> LoadFingerprints(string appKey, string screenId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(screenId);
        lock (_gate)
        {
            if (!TryResolveScreenDirectory(GetAppDirectory(appKey), screenId, out var screenDir))
                return [];

            var stored = ReadJson<StoredScreen>(Path.Combine(screenDir, ScreenFileName));
            return ReadFingerprintsUnlocked(screenDir, stored);
        }
    }

    public bool TryLoadControl(string appKey, string controlId, out CatalogControl control)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(controlId);
        control = null!;
        if (!IsSafeId(controlId))
            return false;

        lock (_gate)
        {
            var screensDir = Path.Combine(GetAppDirectory(appKey), ScreensFolder);
            if (!Directory.Exists(screensDir))
                return false;

            foreach (var screenDir in Directory.GetDirectories(screensDir))
            {
                var storedScreen = ReadJson<StoredScreen>(Path.Combine(screenDir, ScreenFileName));
                if (storedScreen is null)
                    continue;

                var jsonPath = Path.Combine(screenDir, ControlsFolder, controlId + ".json");
                var stored = ReadJson<StoredControl>(jsonPath);
                if (stored is null)
                    continue;

                var pngName = string.IsNullOrWhiteSpace(stored.TemplateFile)
                    ? controlId + ".png"
                    : stored.TemplateFile;
                if (!IsSafeFileName(pngName))
                    return false;

                var pngPath = Path.Combine(screenDir, ControlsFolder, pngName);
                if (!TryDecodePng(pngPath, out var bgra, out var width, out var height))
                    return false;

                control = new CatalogControl(
                    stored.ControlId,
                    stored.Name,
                    storedScreen.ScreenId,
                    stored.Nx,
                    stored.Ny,
                    stored.Nw,
                    stored.Nh,
                    width,
                    height,
                    bgra,
                    stored.SourceWidth,
                    stored.SourceHeight,
                    stored.DpiX,
                    stored.DpiY);
                return true;
            }
        }

        return false;
    }

    public IReadOnlyList<RememberedScreen> List(string appKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appKey);

        lock (_gate)
        {
            var screensDir = Path.Combine(GetAppDirectory(appKey), ScreensFolder);
            if (!Directory.Exists(screensDir))
                return [];

            var listed = new List<RememberedScreen>();
            foreach (var screenDir in Directory.GetDirectories(screensDir))
            {
                var stored = ReadJson<StoredScreen>(Path.Combine(screenDir, ScreenFileName));
                if (stored is null)
                    continue;

                listed.Add(new RememberedScreen(
                    stored.ScreenId,
                    stored.ScreenKey,
                    stored.FingerprintCount,
                    ReadControls(screenDir),
                    stored.LastMatchedAt));
            }

            return listed;
        }
    }

    public IReadOnlyList<CatalogScreenAssets> LoadAppScreens(string appKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appKey);
        lock (_gate)
        {
            var screensDir = Path.Combine(GetAppDirectory(appKey), ScreensFolder);
            if (!Directory.Exists(screensDir))
                return [];

            var loaded = new List<CatalogScreenAssets>();
            foreach (var screenDir in Directory.GetDirectories(screensDir))
            {
                var stored = ReadJson<StoredScreen>(Path.Combine(screenDir, ScreenFileName));
                if (stored is null)
                    continue;

                loaded.Add(new CatalogScreenAssets(
                    stored.ScreenId,
                    stored.ScreenKey,
                    ParsePhash(stored.PhashHex),
                    ReadFingerprintsUnlocked(screenDir, stored),
                    ReadControlLayoutsUnlocked(screenDir, stored.ScreenId, decodePixels: false)));
            }

            return loaded;
        }
    }

    public IReadOnlyList<CatalogControl> LoadScreenControls(string appKey, string screenId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(screenId);
        lock (_gate)
        {
            if (!TryResolveScreenDirectory(GetAppDirectory(appKey), screenId, out var screenDir))
                return [];

            var stored = ReadJson<StoredScreen>(Path.Combine(screenDir, ScreenFileName));
            if (stored is null)
                return [];

            return ReadControlLayoutsUnlocked(screenDir, stored.ScreenId, decodePixels: true);
        }
    }

    public void ForgetScreen(string appKey, string screenId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(screenId);

        WithStoreLock(() =>
        {
            if (!TryResolveScreenDirectory(GetAppDirectory(appKey), screenId, out var screenDir))
                return;

            DeleteScreenDirectoryUnlocked(screenDir);
        });
    }

    public void ForgetControl(string appKey, string controlId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(controlId);
        if (!IsSafeId(controlId))
            return;

        WithStoreLock(() =>
        {
            var screensDir = Path.Combine(GetAppDirectory(appKey), ScreensFolder);
            if (!Directory.Exists(screensDir))
                return;

            foreach (var screenDir in Directory.GetDirectories(screensDir))
            {
                var controlsDir = Path.Combine(screenDir, ControlsFolder);
                if (!Directory.Exists(controlsDir))
                    continue;
                if (!TryResolveContainedLeaf(controlsDir, controlId + ".json", out var jsonPath))
                    continue;
                if (!File.Exists(jsonPath))
                    continue;

                var stored = ReadJson<StoredControl>(jsonPath);
                var pngName = stored is not null && !string.IsNullOrWhiteSpace(stored.TemplateFile)
                    ? stored.TemplateFile
                    : controlId + ".png";
                long removed = 0;
                if (IsSafeFileName(pngName)
                    && TryResolveContainedLeaf(controlsDir, pngName, out var pngPath)
                    && File.Exists(pngPath))
                {
                    removed += FileLength(pngPath);
                    File.Delete(pngPath);
                }

                removed += FileLength(jsonPath);
                File.Delete(jsonPath);
                AccountRemoved(removed);
                return;
            }
        });
    }

    public void TouchMatch(string appKey, string screenId, string controlId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(screenId);
        ArgumentException.ThrowIfNullOrWhiteSpace(controlId);

        WithStoreLock(() =>
        {
            if (!TryResolveScreenDirectory(GetAppDirectory(appKey), screenId, out var screenDir))
                return;

            var now = _utcNow();
            var screenPath = Path.Combine(screenDir, ScreenFileName);
            var storedScreen = ReadJson<StoredScreen>(screenPath);
            if (storedScreen is not null)
            {
                storedScreen.LastMatchedAt = now;
                WriteJsonAccounted(screenPath, storedScreen);
            }

            if (!IsSafeId(controlId))
                return;

            var controlPath = Path.Combine(screenDir, ControlsFolder, controlId + ".json");
            var storedControl = ReadJson<StoredControl>(controlPath);
            if (storedControl is null)
                return;

            storedControl.LastMatchedAt = now;
            WriteJsonAccounted(controlPath, storedControl);
        });
    }

    public bool TryGetAppMetadata(string appKey, out AppCatalogMetadata metadata)
    {
        metadata = default!;
        ArgumentException.ThrowIfNullOrWhiteSpace(appKey);
        lock (_gate)
        {
            var stored = ReadJson<StoredApp>(Path.Combine(GetAppDirectory(appKey), AppFileName));
            if (stored is null)
                return false;

            metadata = new AppCatalogMetadata(
                stored.AppKey,
                stored.PackageFamilyName,
                stored.SignerSubject,
                stored.ProductName,
                stored.ProductVersion,
                stored.ImagePath,
                stored.ClassName);
            return true;
        }
    }

    private string EnsureAppDirectory(string appKey, AppIdentity? diagnostics = null)
    {
        var appDir = GetAppDirectory(appKey);
        Directory.CreateDirectory(appDir);
        var appFile = Path.Combine(appDir, AppFileName);
        if (!File.Exists(appFile))
        {
            WriteJson(appFile, ToStoredApp(appKey, diagnostics));
            AccountWritten(appFile);
        }
        return appDir;
    }

    private static StoredApp ToStoredApp(string appKey, AppIdentity? diagnostics) =>
        new()
        {
            AppKey = appKey,
            PackageFamilyName = diagnostics?.PackageFamilyName,
            SignerSubject = diagnostics?.SignerSubject,
            ProductName = diagnostics?.ProductName,
            ProductVersion = diagnostics?.ProductVersion,
            ImagePath = diagnostics?.RawImagePath ?? diagnostics?.NormalizedImagePath,
            ClassName = diagnostics?.ClassName
        };

    private string GetAppDirectory(string appKey)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(appKey))).ToLowerInvariant();
        return Path.Combine(_root, hash);
    }

    private static bool TryResolveContainedLeaf(string parentDirectory, string leafName, out string fullPath)
    {
        fullPath = "";
        if (string.IsNullOrWhiteSpace(leafName))
            return false;
        if (leafName.Contains(Path.DirectorySeparatorChar) || leafName.Contains(Path.AltDirectorySeparatorChar))
            return false;
        if (leafName is "." or "..")
            return false;

        try
        {
            var parent = Path.GetFullPath(parentDirectory);
            var candidate = Path.GetFullPath(Path.Combine(parent, leafName));
            var parentTrimmed = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var prefix = parentTrimmed + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.Equals(Path.GetFileName(candidate), leafName, StringComparison.Ordinal))
                return false;

            fullPath = candidate;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool TryResolveScreenDirectory(string appDir, string screenId, out string screenDir)
    {
        screenDir = "";
        if (!IsSafeId(screenId))
            return false;

        try
        {
            var screensDir = Path.GetFullPath(Path.Combine(appDir, ScreensFolder));
            var candidate = Path.GetFullPath(Path.Combine(screensDir, screenId));
            var screensTrimmed = screensDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var candidateTrimmed = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(candidateTrimmed, screensTrimmed, StringComparison.OrdinalIgnoreCase))
                return false;

            var prefix = screensTrimmed + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.Equals(Path.GetFileName(candidate), screenId, StringComparison.Ordinal))
                return false;

            screenDir = candidateTrimmed;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return false;
        }
    }

    private static int CountScreens(string appDir)
    {
        var screensDir = Path.Combine(appDir, ScreensFolder);
        return Directory.Exists(screensDir) ? Directory.GetDirectories(screensDir).Length : 0;
    }

    private static int CountControls(string screenDir)
    {
        var controlsDir = Path.Combine(screenDir, ControlsFolder);
        return Directory.Exists(controlsDir) ? Directory.GetFiles(controlsDir, "*.json").Length : 0;
    }

    private static IReadOnlyList<CatalogFingerprint> ReadFingerprintsUnlocked(
        string screenDir,
        StoredScreen? stored)
    {
        if (stored?.Fingerprints is null || stored.Fingerprints.Count == 0)
            return [];

        var loaded = new List<CatalogFingerprint>(stored.Fingerprints.Count);
        var fingerprintDir = Path.Combine(screenDir, FingerprintsFolder);
        foreach (var meta in stored.Fingerprints)
        {
            if (!IsSafeFileName(meta.File))
                continue;
            var path = Path.Combine(fingerprintDir, meta.File);
            if (!TryDecodePng(path, out var bgra, out var width, out var height))
                continue;
            loaded.Add(new CatalogFingerprint(
                meta.X,
                meta.Y,
                width,
                height,
                meta.Nx,
                meta.Ny,
                meta.Nw,
                meta.Nh,
                bgra));
        }

        return loaded;
    }

    private static IReadOnlyList<CatalogControl> ReadControlLayoutsUnlocked(
        string screenDir,
        string screenId,
        bool decodePixels)
    {
        var controlsDir = Path.Combine(screenDir, ControlsFolder);
        if (!Directory.Exists(controlsDir))
            return [];

        var controls = new List<CatalogControl>();
        foreach (var file in Directory.GetFiles(controlsDir, "*.json"))
        {
            var stored = ReadJson<StoredControl>(file);
            if (stored is null)
                continue;

            byte[]? bgra = null;
            var width = stored.Width;
            var height = stored.Height;
            if (decodePixels)
            {
                var pngName = string.IsNullOrWhiteSpace(stored.TemplateFile)
                    ? stored.ControlId + ".png"
                    : stored.TemplateFile;
                if (IsSafeFileName(pngName)
                    && TryDecodePng(Path.Combine(controlsDir, pngName), out var decoded, out var w, out var h))
                {
                    bgra = decoded;
                    width = w;
                    height = h;
                }
            }

            controls.Add(new CatalogControl(
                stored.ControlId,
                stored.Name,
                screenId,
                stored.Nx,
                stored.Ny,
                stored.Nw,
                stored.Nh,
                width,
                height,
                bgra,
                stored.SourceWidth,
                stored.SourceHeight,
                stored.DpiX,
                stored.DpiY));
        }

        return controls;
    }

    private static ulong ParsePhash(string? hex) =>
        hex is not null && ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var bits)
            ? bits
            : 0;

    private static IReadOnlyList<RememberedControl> ReadControls(string screenDir)
    {
        var controlsDir = Path.Combine(screenDir, ControlsFolder);
        if (!Directory.Exists(controlsDir))
            return [];

        var controls = new List<RememberedControl>();
        foreach (var file in Directory.GetFiles(controlsDir, "*.json"))
        {
            var stored = ReadJson<StoredControl>(file);
            if (stored is null)
                continue;
            controls.Add(new RememberedControl(stored.ControlId, stored.Name, stored.LastMatchedAt));
        }

        return controls;
    }

    private void EnsureLibraryQuota(long additionalBytes = 0)
    {
        if (!_usedBytesTrusted)
            RecalibrateUsedBytes();

        if (_usedBytes + additionalBytes >= _limits.MaxMemoryLibraryBytes)
        {
            throw new ComputerUseException(
                ErrorCodes.PayloadTooLarge,
                "The memory library exceeds maxMemoryLibraryBytes.",
                new { maxMemoryLibraryBytes = _limits.MaxMemoryLibraryBytes });
        }
    }

    private void RecalibrateUsedBytes()
    {
        _usedBytes = DirectorySize(_root);
        _usedBytesTrusted = true;
    }

    private void AccountWritten(string path)
    {
        try
        {
            _usedBytes += FileLength(path);
        }
        catch (IOException)
        {
            _usedBytesTrusted = false;
        }
    }

    private void AccountRemoved(long bytes)
    {
        _usedBytes -= bytes;
        if (_usedBytes < 0)
        {
            _usedBytes = 0;
            _usedBytesTrusted = false;
        }
    }

    private static long FileLength(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private static long DirectorySize(string path)
    {
        if (!Directory.Exists(path))
            return 0;

        long size = 0;
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            size += new FileInfo(file).Length;
        return size;
    }

    private static bool IsSafeId(string id)
    {
        if (id.Length is 0 or > 128)
            return false;
        if (id.All(static c => c == '.'))
            return false;
        if (id.Contains(Path.DirectorySeparatorChar) || id.Contains(Path.AltDirectorySeparatorChar))
            return false;

        foreach (var c in id)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_')
                continue;
            return false;
        }

        return true;
    }

    private static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(value, EnvelopeJson.Options);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }

    private static T? ReadJson<T>(string path)
        where T : class
    {
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), EnvelopeJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class StoredApp
    {
        public required string AppKey { get; init; }
        public string? PackageFamilyName { get; init; }
        public string? SignerSubject { get; init; }
        public string? ProductName { get; init; }
        public string? ProductVersion { get; init; }
        public string? ImagePath { get; init; }
        public string? ClassName { get; init; }
    }

    private sealed class StoredScreen
    {
        public required string ScreenId { get; init; }
        public required string ScreenKey { get; init; }
        public int FingerprintCount { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset? LastMatchedAt { get; set; }
        public int Width { get; init; }
        public int Height { get; init; }
        public int SourceWidth { get; init; }
        public int SourceHeight { get; init; }
        public uint DpiX { get; init; }
        public uint DpiY { get; init; }
        public string? PhashHex { get; init; }
        public List<StoredFingerprintMeta>? Fingerprints { get; init; }
    }

    private sealed class StoredFingerprintMeta
    {
        public int Index { get; init; }
        public int X { get; init; }
        public int Y { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public double Nx { get; init; }
        public double Ny { get; init; }
        public double Nw { get; init; }
        public double Nh { get; init; }
        public string File { get; init; } = "";
    }

    private sealed class StoredControl
    {
        public required string ControlId { get; init; }
        public required string Name { get; set; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset? LastMatchedAt { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public double Nx { get; set; }
        public double Ny { get; set; }
        public double Nw { get; set; }
        public double Nh { get; set; }
        public int SourceWidth { get; set; }
        public int SourceHeight { get; set; }
        public uint DpiX { get; set; }
        public uint DpiY { get; set; }
        public string? TemplateFile { get; set; }
    }

    private T WithStoreLock<T>(Func<T> body)
    {
        lock (_gate)
        {
            using var storeLock = AcquireStoreLock();
            return body();
        }
    }

    private void WithStoreLock(Action body) =>
        WithStoreLock<object?>(() =>
        {
            body();
            return null;
        });

    private FileStream AcquireStoreLock()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, LockFileName);
        var sw = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new FileStream(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (sw.Elapsed >= StoreLockTimeout)
                {
                    throw new ComputerUseException(
                        ErrorCodes.Busy,
                        "The memory catalog is busy.",
                        new { timeoutMs = (int)StoreLockTimeout.TotalMilliseconds });
                }

                Thread.Sleep(15);
            }
        }
    }

    private void PrepareWriteUnlocked(string? protectScreenDir, string? screenCountAppDir, long additionalBytes)
    {
        // 只在 remember 写入前淘汰；List 不扫库。未过软 TTL 的 Screen 仍走配额拒绝。
        EvictExpiredUnlocked(protectScreenDir);
        EvictExpiredForQuotaUnlocked(protectScreenDir, screenCountAppDir, additionalBytes);
    }

    private void EvictExpiredUnlocked(string? protectScreenDir)
    {
        var ttl = SoftTtl();
        if (ttl is null)
            return;

        var now = _utcNow();
        foreach (var screen in EnumerateScreensUnlocked())
        {
            if (IsProtected(protectScreenDir, screen.Dir))
                continue;
            if (now - screen.Activity >= ttl.Value)
                DeleteScreenDirectoryUnlocked(screen.Dir);
        }
    }

    private void EvictExpiredForQuotaUnlocked(string? protectScreenDir, string? screenCountAppDir, long additionalBytes)
    {
        var ttl = SoftTtl();
        if (ttl is null)
            return;

        var now = _utcNow();
        bool Eligible(ScreenEntry screen) =>
            !IsProtected(protectScreenDir, screen.Dir) && now - screen.Activity >= ttl.Value;

        if (screenCountAppDir is not null)
        {
            while (CountScreens(screenCountAppDir) >= _limits.MaxScreensPerAppKey)
            {
                var victim = PickExpiredVictim(
                    Eligible,
                    screen => string.Equals(screen.AppDir, screenCountAppDir, StringComparison.OrdinalIgnoreCase));
                if (victim is null)
                    break;
                DeleteScreenDirectoryUnlocked(victim.Dir);
            }
        }

        while (_usedBytes + additionalBytes >= _limits.MaxMemoryLibraryBytes)
        {
            var victim = PickExpiredVictim(Eligible, _ => true);
            if (victim is null)
                break;
            DeleteScreenDirectoryUnlocked(victim.Dir);
        }
    }

    private ScreenEntry? PickExpiredVictim(Func<ScreenEntry, bool> eligible, Func<ScreenEntry, bool> scope)
    {
        ScreenEntry? best = null;
        long bestSize = -1;
        foreach (var screen in EnumerateScreensUnlocked())
        {
            if (!scope(screen) || !eligible(screen))
                continue;
            var size = DirectorySize(screen.Dir);
            if (best is null
                || screen.Activity < best.Activity
                || (screen.Activity == best.Activity && size > bestSize))
            {
                best = screen;
                bestSize = size;
            }
        }

        return best;
    }

    private TimeSpan? SoftTtl()
    {
        if (_limits.MemorySoftTtlDays <= 0)
            return null;
        return TimeSpan.FromDays(_limits.MemorySoftTtlDays);
    }

    private static bool IsProtected(string? protectScreenDir, string screenDir) =>
        protectScreenDir is not null
        && string.Equals(protectScreenDir, screenDir, StringComparison.OrdinalIgnoreCase);

    private IEnumerable<ScreenEntry> EnumerateScreensUnlocked()
    {
        if (!Directory.Exists(_root))
            yield break;

        foreach (var appDir in Directory.GetDirectories(_root))
        {
            var screensDir = Path.Combine(appDir, ScreensFolder);
            if (!Directory.Exists(screensDir))
                continue;

            foreach (var screenDir in Directory.GetDirectories(screensDir))
            {
                var stored = ReadJson<StoredScreen>(Path.Combine(screenDir, ScreenFileName));
                if (stored is null)
                    continue;
                yield return new ScreenEntry(screenDir, appDir, stored.LastMatchedAt ?? stored.CreatedAt);
            }
        }
    }

    private void DeleteScreenDirectoryUnlocked(string screenDir)
    {
        if (!Directory.Exists(screenDir))
            return;

        var size = DirectorySize(screenDir);
        Directory.Delete(screenDir, recursive: true);
        AccountRemoved(size);
    }

    private static bool TryFindControlByName(
        string screenDir,
        string name,
        out StoredControl stored,
        out string jsonPath)
    {
        stored = null!;
        jsonPath = "";
        var controlsDir = Path.Combine(screenDir, ControlsFolder);
        if (!Directory.Exists(controlsDir))
            return false;

        foreach (var file in Directory.GetFiles(controlsDir, "*.json"))
        {
            var candidate = ReadJson<StoredControl>(file);
            if (candidate is null)
                continue;
            if (!string.Equals(candidate.Name, name, StringComparison.Ordinal))
                continue;

            stored = candidate;
            jsonPath = file;
            return true;
        }

        return false;
    }

    private void WriteJsonAccounted<T>(string path, T value)
    {
        var previous = FileLength(path);
        WriteJson(path, value);
        if (previous > 0)
            AccountRemoved(previous);
        AccountWritten(path);
    }

    private void WriteBytesAccounted(string path, byte[] bytes)
    {
        var previous = FileLength(path);
        File.WriteAllBytes(path, bytes);
        if (previous > 0)
            AccountRemoved(previous);
        AccountWritten(path);
    }

    private static void TryRestrictDirectoryAcl(string directory)
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var sid = identity.User?.Value;
            if (string.IsNullOrWhiteSpace(sid))
                return;

            // 当前用户 + SYSTEM 完全控制；P 关掉 Users/Everyone 继承。
            var sddl = $"D:P(A;OICI;FA;;;{sid})(A;OICI;FA;;;SY)";
            if (!ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, 1, out var sd, out _))
                return;
            try
            {
                SetFileSecurity(
                    directory,
                    DaclSecurityInformation | ProtectedDaclSecurityInformation,
                    sd);
            }
            finally
            {
                LocalFree(sd);
            }
        }
        catch
        {
            // ACL 失败不得阻止 MCP 启动或测试建库。
        }
    }

    private const uint DaclSecurityInformation = 0x00000004;
    private const uint ProtectedDaclSecurityInformation = 0x80000000;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(
        string stringSecurityDescriptor,
        uint stringSdRevision,
        out IntPtr securityDescriptor,
        out uint securityDescriptorSize);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetFileSecurity(
        string fileName,
        uint securityInformation,
        IntPtr securityDescriptor);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);

    private sealed record ScreenEntry(string Dir, string AppDir, DateTimeOffset Activity);

    private static long SumPngBytes(IReadOnlyList<FingerprintAsset> fingerprints)
    {
        long total = 0;
        foreach (var fingerprint in fingerprints)
            total += fingerprint.Png?.Length ?? 0;
        return total;
    }

    private static bool IsSafeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;
        if (fileName.Contains(Path.DirectorySeparatorChar) || fileName.Contains(Path.AltDirectorySeparatorChar))
            return false;
        if (fileName is "." or "..")
            return false;
        return IsSafeId(Path.GetFileNameWithoutExtension(fileName))
            && string.Equals(Path.GetExtension(fileName), ".png", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryDecodePng(string path, out byte[] bgra, out int width, out int height)
    {
        bgra = [];
        width = 0;
        height = 0;
        if (!File.Exists(path))
            return false;

        try
        {
            using var ms = new MemoryStream(File.ReadAllBytes(path));
            using var bmp = new Bitmap(ms);
            width = bmp.Width;
            height = bmp.Height;
            bgra = CopyPackedBgra(bmp);
            return width > 0 && height > 0;
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or IOException)
        {
            return false;
        }
    }

    private static byte[] CopyPackedBgra(Bitmap bmp)
    {
        var width = bmp.Width;
        var height = bmp.Height;
        var destStride = checked(width * 4);
        var dest = new byte[checked(destStride * height)];
        var data = bmp.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, dest, y * destStride, destStride);
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        return dest;
    }
}

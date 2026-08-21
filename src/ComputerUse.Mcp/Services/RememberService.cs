using ComputerUse.Mcp.Capture;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Memory;
using ComputerUse.Mcp.Vision;

namespace ComputerUse.Mcp.Services;

internal readonly record struct PixelBox(int X, int Y, int Width, int Height);

internal sealed class RememberService
{
    private const int TinyDialogMaxEdgePx = 200;
    private const double MinCenterSpreadFraction = 0.25;

    private readonly MemoryCatalog _catalog;
    private readonly Limits _limits;

    public RememberService(MemoryCatalog catalog, Limits limits)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(limits);
        _catalog = catalog;
        _limits = limits;
    }

    public string RememberScreen(
        FrameRecord frame,
        string appKey,
        string screenKey,
        IReadOnlyList<PixelBox> fingerprints,
        bool hostWindow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(screenKey);
        ArgumentNullException.ThrowIfNull(fingerprints);

        EnsureRememberAllowed(frame, hostWindow);
        EnsureFingerprintLayout(frame.Width, frame.Height, fingerprints);

        var crops = ExtractCrops(frame, fingerprints);
        if (TryFindExistingScreen(frame, appKey, crops.Count, out var existingId))
            return existingId;

        var assets = new FingerprintAsset[crops.Count];
        for (var i = 0; i < crops.Count; i++)
        {
            var crop = crops[i];
            var png = PngCodec.EncodeBgra(crop.Packed, crop.Width, crop.Height, crop.Width * 4);
            var (nx, ny, nw, nh) = Normalize(crop.Box, frame.Width, frame.Height);
            assets[i] = new FingerprintAsset(
                crop.Box.X,
                crop.Box.Y,
                crop.Width,
                crop.Height,
                png,
                nx,
                ny,
                nw,
                nh);
        }

        var hash = PerceptualHash.Compute(frame.Bgra!, frame.Width, frame.Height, frame.BgraStride);
        var snapshot = new ScreenSnapshot(
            frame.Width,
            frame.Height,
            frame.SourceWidth,
            frame.SourceHeight,
            frame.Dpi.X,
            frame.Dpi.Y,
            hash.Bits);

        return _catalog.PutScreen(appKey, screenKey, assets, snapshot);
    }

    public string RememberControl(
        FrameRecord frame,
        string appKey,
        string screenId,
        string name,
        PixelBox box,
        bool hostWindow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(screenId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        EnsureRememberAllowed(frame, hostWindow);
        if (!_catalog.ScreenExists(appKey, screenId))
        {
            throw new ComputerUseException(
                ErrorCodes.UnknownControl,
                "The screenId is unknown for this AppKey.");
        }

        var crop = ExtractCrop(frame, box);
        var png = PngCodec.EncodeBgra(crop.Packed, crop.Width, crop.Height, crop.Width * 4);
        var (nx, ny, nw, nh) = Normalize(box, frame.Width, frame.Height);
        var asset = new ControlAsset(
            png,
            crop.Width,
            crop.Height,
            nx,
            ny,
            nw,
            nh,
            frame.SourceWidth,
            frame.SourceHeight,
            frame.Dpi.X,
            frame.Dpi.Y);

        return _catalog.PutControl(appKey, screenId, name, asset);
    }

    private static void EnsureRememberAllowed(FrameRecord frame, bool hostWindow)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (hostWindow)
        {
            throw new ComputerUseException(
                ErrorCodes.HostWindowForbidden,
                "HostWindow cannot be stored in control memory.");
        }

        if (!frame.ImageReturnedToClient)
        {
            throw new ComputerUseException(
                ErrorCodes.FrameNotVisualized,
                "remember requires a frame that was returned as an image to the client.");
        }

        if (frame.Bgra is null
            || frame.Bgra.Length == 0
            || frame.Width <= 0
            || frame.Height <= 0
            || frame.BgraStride < checked(frame.Width * 4))
        {
            throw new ComputerUseException(
                ErrorCodes.StaleCapture,
                "The visualized frame no longer has pixels.");
        }
    }

    private static void EnsureFingerprintLayout(
        int width,
        int height,
        IReadOnlyList<PixelBox> fingerprints)
    {
        if (fingerprints.Count == 0)
        {
            throw new ComputerUseException(
                ErrorCodes.InvalidAction,
                "At least one fingerprint is required.");
        }

        var tiny = width < TinyDialogMaxEdgePx && height < TinyDialogMaxEdgePx;
        if (!tiny && fingerprints.Count < 2)
        {
            throw new ComputerUseException(
                ErrorCodes.InvalidAction,
                "Large windows require at least two spatially spread fingerprints.");
        }

        if (fingerprints.Count >= 2 && !AreSpatiallySpread(width, height, fingerprints))
        {
            throw new ComputerUseException(
                ErrorCodes.InvalidAction,
                "Fingerprint boxes must be spatially spread (center distance at least 25% of the shorter window edge).");
        }
    }

    private static bool AreSpatiallySpread(int width, int height, IReadOnlyList<PixelBox> fingerprints)
    {
        var minDistance = MinCenterSpreadFraction * Math.Min(width, height);
        var maxDistance = 0.0;
        for (var i = 0; i < fingerprints.Count; i++)
        {
            var a = Center(fingerprints[i]);
            for (var j = i + 1; j < fingerprints.Count; j++)
            {
                var b = Center(fingerprints[j]);
                var dx = a.X - b.X;
                var dy = a.Y - b.Y;
                maxDistance = Math.Max(maxDistance, Math.Sqrt(dx * dx + dy * dy));
            }
        }

        return maxDistance + 1e-9 >= minDistance;
    }

    private static (double X, double Y) Center(PixelBox box) =>
        (box.X + box.Width / 2.0, box.Y + box.Height / 2.0);

    private List<CroppedPatch> ExtractCrops(FrameRecord frame, IReadOnlyList<PixelBox> boxes)
    {
        var crops = new List<CroppedPatch>(boxes.Count);
        foreach (var box in boxes)
            crops.Add(ExtractCrop(frame, box));
        return crops;
    }

    private CroppedPatch ExtractCrop(FrameRecord frame, PixelBox box)
    {
        var packed = CropEntropy.ExtractValidated(
            frame.Bgra!,
            frame.Width,
            frame.Height,
            frame.BgraStride,
            box.X,
            box.Y,
            box.Width,
            box.Height);

        if (Math.Max(box.Width, box.Height) > _limits.MaxTemplateLongEdge)
        {
            throw new ComputerUseException(
                ErrorCodes.PayloadTooLarge,
                $"The crop long edge exceeds maxTemplateLongEdge ({_limits.MaxTemplateLongEdge}).");
        }

        return new CroppedPatch(box, packed, box.Width, box.Height);
    }

    private bool TryFindExistingScreen(
        FrameRecord frame,
        string appKey,
        int fingerprintCount,
        out string screenId)
    {
        foreach (var screen in _catalog.List(appKey))
        {
            var stored = _catalog.LoadFingerprints(appKey, screen.ScreenId);
            if (stored.Count == 0 || stored.Count != fingerprintCount)
                continue;
            if (AllFingerprintsMatch(frame, stored))
            {
                screenId = screen.ScreenId;
                return true;
            }
        }

        screenId = "";
        return false;
    }

    private bool AllFingerprintsMatch(FrameRecord frame, IReadOnlyList<CatalogFingerprint> stored)
    {
        foreach (var fingerprint in stored)
        {
            var result = ZnccMatcher.Match(
                frame.Bgra!,
                frame.Width,
                frame.Height,
                frame.BgraStride,
                fingerprint.Bgra,
                fingerprint.Width,
                fingerprint.Height,
                fingerprint.Width * 4,
                _limits.TemplateScaleMin,
                _limits.TemplateScaleMax);
            if (result.Status != TemplateMatchStatus.Found)
                return false;
        }

        return true;
    }

    private static (double Nx, double Ny, double Nw, double Nh) Normalize(
        PixelBox box, int frameWidth, int frameHeight) =>
        (
            box.X / (double)frameWidth,
            box.Y / (double)frameHeight,
            box.Width / (double)frameWidth,
            box.Height / (double)frameHeight);

    private readonly record struct CroppedPatch(PixelBox Box, byte[] Packed, int Width, int Height);
}

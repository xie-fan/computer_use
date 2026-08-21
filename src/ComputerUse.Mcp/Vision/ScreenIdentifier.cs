using ComputerUse.Mcp.Domain;

namespace ComputerUse.Mcp.Vision;

internal enum ScreenIdentifyStatus
{
    Unknown,
    Identified,
    Ambiguous,
    Mismatch
}

internal sealed record ScreenFingerprint(int X, int Y, int Width, int Height, byte[] Bgra);

internal sealed record StoredControlLayout(string ControlId, double Nx, double Ny, double Nw, double Nh);

internal sealed record StoredScreenCatalogEntry(
    string ScreenId,
    PerceptualHashValue WholeWindowHash,
    IReadOnlyList<ScreenFingerprint> Fingerprints,
    IReadOnlyList<StoredControlLayout> Controls);

internal sealed record ScreenIdentifyResult(
    ScreenIdentifyStatus Status,
    string? ScreenId,
    IReadOnlyList<string> CandidateIds);

internal static class ScreenIdentifier
{
    private const int MaxNominatedCandidates = 3;
    private const double RoiExpandFactor = 0.20;
    private const double LayoutCenterTolerance = 0.15;

    public static ScreenIdentifyResult Identify(
        byte[] frameBgra,
        int width,
        int height,
        int stride,
        IReadOnlyList<StoredScreenCatalogEntry> library,
        string? requiredScreenId = null)
    {
        ArgumentNullException.ThrowIfNull(frameBgra);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, checked(width * 4));

        if (library.Count == 0)
            return Finalize(ScreenIdentifyStatus.Unknown, null, [], requiredScreenId);

        var query = PerceptualHash.Compute(frameBgra, width, height, stride);
        var hashLibrary = new (string Id, PerceptualHashValue Hash)[library.Count];
        var byId = new Dictionary<string, StoredScreenCatalogEntry>(library.Count, StringComparer.Ordinal);
        for (var i = 0; i < library.Count; i++)
        {
            var entry = library[i];
            hashLibrary[i] = (entry.ScreenId, entry.WholeWindowHash);
            byId[entry.ScreenId] = entry;
        }

        var nominated = PerceptualHash.Nominate(query, hashLibrary, MaxNominatedCandidates);
        var candidateIds = new string[nominated.Count];
        var survivors = new List<string>();
        for (var i = 0; i < nominated.Count; i++)
        {
            var id = nominated[i].Id;
            candidateIds[i] = id;
            if (!byId.TryGetValue(id, out var entry))
                continue;
            if (CandidateSurvives(frameBgra, width, height, stride, entry))
                survivors.Add(id);
        }

        if (survivors.Count == 1)
            return Finalize(ScreenIdentifyStatus.Identified, survivors[0], candidateIds, requiredScreenId);
        if (survivors.Count == 0)
            return Finalize(ScreenIdentifyStatus.Unknown, null, candidateIds, requiredScreenId);
        return Finalize(ScreenIdentifyStatus.Ambiguous, null, candidateIds, requiredScreenId);
    }

    private static ScreenIdentifyResult Finalize(
        ScreenIdentifyStatus status,
        string? screenId,
        IReadOnlyList<string> candidateIds,
        string? requiredScreenId)
    {
        if (requiredScreenId is null)
            return new ScreenIdentifyResult(status, screenId, candidateIds);

        if (status == ScreenIdentifyStatus.Identified
            && string.Equals(screenId, requiredScreenId, StringComparison.Ordinal))
            return new ScreenIdentifyResult(status, screenId, candidateIds);

        return new ScreenIdentifyResult(ScreenIdentifyStatus.Mismatch, screenId, candidateIds);
    }

    private static bool CandidateSurvives(
        byte[] frameBgra,
        int width,
        int height,
        int stride,
        StoredScreenCatalogEntry entry)
    {
        var requiredMatches = entry.Fingerprints.Count <= 1 ? 1 : 2;
        if (entry.Fingerprints.Count < requiredMatches)
            return false;

        var matches = new MatchedBox[entry.Fingerprints.Count];
        for (var i = 0; i < entry.Fingerprints.Count; i++)
        {
            if (!TryMatchFingerprint(frameBgra, width, height, stride, entry.Fingerprints[i], out matches[i]))
                return false;
        }

        if (entry.Controls.Count == 0)
            return true;

        return LayoutHolds(width, height, entry.Fingerprints, matches, entry.Controls);
    }

    private static bool LayoutHolds(
        int width,
        int height,
        IReadOnlyList<ScreenFingerprint> fingerprints,
        IReadOnlyList<MatchedBox> matches,
        IReadOnlyList<StoredControlLayout> controls)
    {
        for (var i = 0; i < fingerprints.Count; i++)
        {
            var fp = fingerprints[i];
            var match = matches[i];
            var storedNx = (fp.X + fp.Width * 0.5) / width;
            var storedNy = (fp.Y + fp.Height * 0.5) / height;
            var foundNx = (match.X + match.Width * 0.5) / width;
            var foundNy = (match.Y + match.Height * 0.5) / height;
            if (Math.Abs(storedNx - foundNx) > LayoutCenterTolerance
                || Math.Abs(storedNy - foundNy) > LayoutCenterTolerance)
                return false;
        }

        foreach (var control in controls)
        {
            if (control.Nw <= 0 || control.Nh <= 0)
                return false;

            var cx = control.Nx + control.Nw * 0.5;
            var cy = control.Ny + control.Nh * 0.5;
            if (cx < -LayoutCenterTolerance || cx > 1.0 + LayoutCenterTolerance
                || cy < -LayoutCenterTolerance || cy > 1.0 + LayoutCenterTolerance)
                return false;
        }

        return true;
    }

    private static bool TryMatchFingerprint(
        byte[] frame,
        int width,
        int height,
        int stride,
        ScreenFingerprint fingerprint,
        out MatchedBox match)
    {
        match = default;
        if (fingerprint.Width <= 0 || fingerprint.Height <= 0 || fingerprint.Bgra is null)
            return false;

        var templateStride = checked(fingerprint.Width * 4);
        if (fingerprint.Bgra.Length < checked(templateStride * fingerprint.Height))
            return false;

        var roi = ExpandRoi(fingerprint.X, fingerprint.Y, fingerprint.Width, fingerprint.Height, width, height);
        var roiLargeEnough = roi.Width >= fingerprint.Width && roi.Height >= fingerprint.Height;
        if (roiLargeEnough
            && TryMatchHaystack(frame, width, height, stride, roi, fingerprint, templateStride, out match))
            return true;

        var fullFrame = roi.X == 0 && roi.Y == 0 && roi.Width == width && roi.Height == height;
        if (fullFrame)
            return false;

        return TryMatchHaystack(
            frame, width, height, stride,
            new SearchRect(0, 0, width, height),
            fingerprint,
            templateStride,
            out match);
    }

    private static bool TryMatchHaystack(
        byte[] frame,
        int width,
        int height,
        int stride,
        SearchRect roi,
        ScreenFingerprint fingerprint,
        int templateStride,
        out MatchedBox match)
    {
        match = default;
        if (roi.Width < fingerprint.Width || roi.Height < fingerprint.Height)
            return false;

        byte[] haystack;
        int hayWidth;
        int hayHeight;
        int hayStride;
        if (roi.X == 0 && roi.Y == 0 && roi.Width == width && roi.Height == height)
        {
            haystack = frame;
            hayWidth = width;
            hayHeight = height;
            hayStride = stride;
        }
        else
        {
            haystack = CropPacked(frame, stride, roi.X, roi.Y, roi.Width, roi.Height);
            hayWidth = roi.Width;
            hayHeight = roi.Height;
            hayStride = checked(roi.Width * 4);
        }

        var result = ZnccMatcher.Match(
            haystack,
            hayWidth,
            hayHeight,
            hayStride,
            fingerprint.Bgra,
            fingerprint.Width,
            fingerprint.Height,
            templateStride,
            Limits.V1.TemplateScaleMin,
            Limits.V1.TemplateScaleMax);

        if (result.Status != TemplateMatchStatus.Found)
            return false;

        match = new MatchedBox(
            roi.X + result.X,
            roi.Y + result.Y,
            result.Width,
            result.Height,
            result.Score);
        return true;
    }

    private static SearchRect ExpandRoi(int x, int y, int width, int height, int frameWidth, int frameHeight)
    {
        var padX = (int)Math.Ceiling(width * RoiExpandFactor / 2.0);
        var padY = (int)Math.Ceiling(height * RoiExpandFactor / 2.0);
        var left = Math.Clamp(x - padX, 0, frameWidth);
        var top = Math.Clamp(y - padY, 0, frameHeight);
        var right = Math.Clamp(x + width + padX, 0, frameWidth);
        var bottom = Math.Clamp(y + height + padY, 0, frameHeight);
        return new SearchRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static byte[] CropPacked(byte[] bgra, int stride, int x, int y, int cropWidth, int cropHeight)
    {
        var rowBytes = checked(cropWidth * 4);
        var dest = new byte[checked(rowBytes * cropHeight)];
        var srcOrigin = checked(y * stride + x * 4);
        for (var row = 0; row < cropHeight; row++)
        {
            Buffer.BlockCopy(
                bgra,
                checked(srcOrigin + row * stride),
                dest,
                checked(row * rowBytes),
                rowBytes);
        }

        return dest;
    }

    private readonly record struct SearchRect(int X, int Y, int Width, int Height);

    private readonly record struct MatchedBox(int X, int Y, int Width, int Height, double Score);
}

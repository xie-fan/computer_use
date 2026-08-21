using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Memory;

namespace ComputerUse.Mcp.Vision;

internal enum ScreenIdentifyStatus
{
    Unknown,
    Identified,
    Ambiguous,
    Mismatch
}

internal sealed record ScreenFingerprint(
    int X,
    int Y,
    int Width,
    int Height,
    double Nx,
    double Ny,
    double Nw,
    double Nh,
    byte[] Bgra);

internal sealed record StoredControlLayout(
    string ControlId,
    double Nx,
    double Ny,
    double Nw,
    double Nh,
    int Width,
    int Height,
    byte[]? Bgra);

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
    // ZNCC/pHash 忽略直流分量，不同噪声种子仍可能匹配；绝对 MAE 拒绝这类貌合神离。
    private const double MaxFingerprintMae = 16;

    public static IReadOnlyList<StoredScreenCatalogEntry> FromCatalog(IReadOnlyList<CatalogScreenAssets> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);
        var library = new StoredScreenCatalogEntry[screens.Count];
        for (var i = 0; i < screens.Count; i++)
        {
            var screen = screens[i];
            var fingerprints = new ScreenFingerprint[screen.Fingerprints.Count];
            for (var f = 0; f < screen.Fingerprints.Count; f++)
            {
                var fp = screen.Fingerprints[f];
                fingerprints[f] = new ScreenFingerprint(
                    fp.X,
                    fp.Y,
                    fp.Width,
                    fp.Height,
                    fp.Nx,
                    fp.Ny,
                    fp.Nw,
                    fp.Nh,
                    fp.Bgra);
            }

            var controls = new StoredControlLayout[screen.Controls.Count];
            for (var c = 0; c < screen.Controls.Count; c++)
            {
                var stored = screen.Controls[c];
                controls[c] = new StoredControlLayout(
                    stored.ControlId,
                    stored.Nx,
                    stored.Ny,
                    stored.Nw,
                    stored.Nh,
                    stored.Width,
                    stored.Height,
                    stored.Bgra);
            }

            library[i] = new StoredScreenCatalogEntry(
                screen.ScreenId,
                new PerceptualHashValue(screen.PhashBits),
                fingerprints,
                controls);
        }

        return library;
    }

    public static IReadOnlyList<StoredControlLayout> ControlsFrom(IReadOnlyList<CatalogControl> controls)
    {
        ArgumentNullException.ThrowIfNull(controls);
        var layouts = new StoredControlLayout[controls.Count];
        for (var i = 0; i < controls.Count; i++)
        {
            var stored = controls[i];
            layouts[i] = new StoredControlLayout(
                stored.ControlId,
                stored.Nx,
                stored.Ny,
                stored.Nw,
                stored.Nh,
                stored.Width,
                stored.Height,
                stored.Bgra);
        }

        return layouts;
    }

    public static ScreenIdentifyResult Identify(
        byte[] frameBgra,
        int width,
        int height,
        int stride,
        IReadOnlyList<StoredScreenCatalogEntry> library,
        string? requiredScreenId = null,
        Func<string, IReadOnlyList<StoredControlLayout>>? loadNominatedControls = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frameBgra);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, checked(width * 4));

        if (library.Count == 0)
            return Finalize(ScreenIdentifyStatus.Unknown, null, [], requiredScreenId);

        cancellationToken.ThrowIfCancellationRequested();
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
            cancellationToken.ThrowIfCancellationRequested();
            var id = nominated[i].Id;
            candidateIds[i] = id;
            if (!byId.TryGetValue(id, out var entry))
                continue;

            // 提名后再加载这 1–3 个候选的 Control PNG，供 LayoutHolds MAE（#9）。
            var controls = loadNominatedControls is null
                ? entry.Controls
                : loadNominatedControls(id);
            if (controls.Count == 0)
                controls = entry.Controls;

            var hydrated = entry with { Controls = controls };
            if (CandidateSurvives(frameBgra, width, height, stride, hydrated, cancellationToken))
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
        StoredScreenCatalogEntry entry,
        CancellationToken cancellationToken)
    {
        var requiredMatches = entry.Fingerprints.Count <= 1 ? 1 : 2;
        if (entry.Fingerprints.Count < requiredMatches)
            return false;

        var matches = new MatchedBox[entry.Fingerprints.Count];
        for (var i = 0; i < entry.Fingerprints.Count; i++)
        {
            if (!TryMatchFingerprint(
                    frameBgra, width, height, stride, entry.Fingerprints[i], cancellationToken, out matches[i]))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return false;
            }
        }

        if (!FingerprintsSurviveMae(frameBgra, width, height, stride, entry.Fingerprints))
            return false;

        if (entry.Controls.Count == 0)
            return true;

        return LayoutHolds(frameBgra, width, height, stride, entry.Fingerprints, matches, entry.Controls);
    }

    private static bool LayoutHolds(
        byte[] frameBgra,
        int width,
        int height,
        int stride,
        IReadOnlyList<ScreenFingerprint> fingerprints,
        IReadOnlyList<MatchedBox> matches,
        IReadOnlyList<StoredControlLayout> controls)
    {
        for (var i = 0; i < fingerprints.Count; i++)
        {
            var fp = fingerprints[i];
            var match = matches[i];
            // 用入库归一化中心，禁止 (fp.X + w/2) / 当前帧宽（缩放后会漂）。
            var storedNx = fp.Nx + fp.Nw * 0.5;
            var storedNy = fp.Ny + fp.Nh * 0.5;
            var foundNx = (match.X + match.Width * 0.5) / width;
            var foundNy = (match.Y + match.Height * 0.5) / height;
            if (Math.Abs(storedNx - foundNx) > LayoutCenterTolerance
                || Math.Abs(storedNy - foundNy) > LayoutCenterTolerance)
                return false;
        }

        // 主指纹作原点，避免远端指纹 ZNCC 抖动把 expected 框带偏。
        var storedFpX = fingerprints[0].Nx + fingerprints[0].Nw * 0.5;
        var storedFpY = fingerprints[0].Ny + fingerprints[0].Nh * 0.5;
        var foundFpX = (matches[0].X + matches[0].Width * 0.5) / width;
        var foundFpY = (matches[0].Y + matches[0].Height * 0.5) / height;

        var hasControlPixels = false;
        var anyControlMae = false;
        foreach (var control in controls)
        {
            if (control.Nw <= 0 || control.Nh <= 0)
                return false;
            if (control.Bgra is null || control.Width <= 0 || control.Height <= 0)
                continue;

            hasControlPixels = true;
            var storedCx = control.Nx + control.Nw * 0.5;
            var storedCy = control.Ny + control.Nh * 0.5;
            // expected = 当前指纹中心 + (入库 Control 中心 − 入库指纹中心)
            var expectedCx = foundFpX + (storedCx - storedFpX);
            var expectedCy = foundFpY + (storedCy - storedFpY);
            var originX = (int)Math.Floor((expectedCx - control.Nw * 0.5) * width);
            var originY = (int)Math.Floor((expectedCy - control.Nh * 0.5) * height);
            // expected 来自 ZNCC 框，允许 ±2px 吸收取整/尺度金字塔抖动；打乱 Nx 后框会偏出很远。
            if (PatchMaeNear(frameBgra, width, height, stride, control.Bgra, control.Width, control.Height, originX, originY)
                <= MaxFingerprintMae)
                anyControlMae = true;
        }

        // 没有任何 Control 带 BGRA：只靠指纹。有像素则至少一个 MAE≤16。
        return !hasControlPixels || anyControlMae;
    }

    private static bool FingerprintsSurviveMae(
        byte[] frameBgra,
        int width,
        int height,
        int stride,
        IReadOnlyList<ScreenFingerprint> fingerprints)
    {
        var required = fingerprints.Count <= 1 ? 1 : 2;
        var aligned = 0;
        foreach (var fingerprint in fingerprints)
        {
            if (FingerprintMae(frameBgra, width, height, stride, fingerprint) <= MaxFingerprintMae)
                aligned++;
        }

        return aligned >= required;
    }

    private static double FingerprintMae(
        byte[] frameBgra,
        int width,
        int height,
        int stride,
        ScreenFingerprint fingerprint)
    {
        if (fingerprint.Bgra is null || fingerprint.Width <= 0 || fingerprint.Height <= 0)
            return double.PositiveInfinity;

        var x = (int)Math.Floor(fingerprint.Nx * width);
        var y = (int)Math.Floor(fingerprint.Ny * height);
        return PatchMae(frameBgra, width, height, stride, fingerprint.Bgra, fingerprint.Width, fingerprint.Height, x, y);
    }

    private static double PatchMaeNear(
        byte[] frameBgra,
        int width,
        int height,
        int stride,
        byte[] template,
        int templateWidth,
        int templateHeight,
        int originX,
        int originY)
    {
        const int radius = 2;
        var best = double.PositiveInfinity;
        for (var dy = -radius; dy <= radius; dy++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                var mae = PatchMae(
                    frameBgra, width, height, stride,
                    template, templateWidth, templateHeight,
                    originX + dx, originY + dy);
                if (mae < best)
                    best = mae;
            }
        }

        return best;
    }

    private static double PatchMae(
        byte[] frameBgra,
        int width,
        int height,
        int stride,
        byte[] template,
        int templateWidth,
        int templateHeight,
        int originX,
        int originY)
    {
        if (templateWidth <= 0 || templateHeight <= 0)
            return double.PositiveInfinity;
        if (originX < 0 || originY < 0
            || originX + templateWidth > width
            || originY + templateHeight > height)
            return double.PositiveInfinity;

        var templateStride = checked(templateWidth * 4);
        if (template.Length < checked(templateStride * templateHeight))
            return double.PositiveInfinity;

        long total = 0;
        var count = 0;
        for (var row = 0; row < templateHeight; row++)
        {
            var src = (originY + row) * stride + originX * 4;
            var tmpl = row * templateStride;
            for (var col = 0; col < templateWidth; col++)
            {
                var si = src + col * 4;
                var ti = tmpl + col * 4;
                total += Math.Abs(frameBgra[si] - template[ti]);
                total += Math.Abs(frameBgra[si + 1] - template[ti + 1]);
                total += Math.Abs(frameBgra[si + 2] - template[ti + 2]);
                count += 3;
            }
        }

        return count == 0 ? double.PositiveInfinity : total / (double)count;
    }

    private static bool TryMatchFingerprint(
        byte[] frame,
        int width,
        int height,
        int stride,
        ScreenFingerprint fingerprint,
        CancellationToken cancellationToken,
        out MatchedBox match)
    {
        match = default;
        if (fingerprint.Width <= 0 || fingerprint.Height <= 0 || fingerprint.Bgra is null)
            return false;

        var templateStride = checked(fingerprint.Width * 4);
        if (fingerprint.Bgra.Length < checked(templateStride * fingerprint.Height))
            return false;

        var roiX = (int)Math.Floor(fingerprint.Nx * width);
        var roiY = (int)Math.Floor(fingerprint.Ny * height);
        var roiW = Math.Max(1, (int)Math.Round(fingerprint.Nw * width));
        var roiH = Math.Max(1, (int)Math.Round(fingerprint.Nh * height));
        var roi = ExpandRoi(roiX, roiY, roiW, roiH, width, height);
        var roiLargeEnough = roi.Width >= fingerprint.Width && roi.Height >= fingerprint.Height;
        if (roiLargeEnough
            && TryMatchHaystack(
                frame, width, height, stride, roi, fingerprint, templateStride, cancellationToken, out match))
            return true;

        var fullFrame = roi.X == 0 && roi.Y == 0 && roi.Width == width && roi.Height == height;
        if (fullFrame)
            return false;
        if (ZnccMatcher.ShouldSkipFullFrameFallback(fingerprint.Width, fingerprint.Height, width, height))
            return false;

        return TryMatchHaystack(
            frame, width, height, stride,
            new SearchRect(0, 0, width, height),
            fingerprint,
            templateStride,
            cancellationToken,
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
        CancellationToken cancellationToken,
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
            Limits.V1.TemplateScaleMax,
            cancellationToken);

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

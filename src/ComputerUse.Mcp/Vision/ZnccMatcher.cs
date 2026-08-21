using ComputerUse.Mcp.Domain;

namespace ComputerUse.Mcp.Vision;

internal enum TemplateMatchStatus
{
    Found,
    NotFound,
    Ambiguous,
    ScaleMismatch
}

internal sealed record TemplateMatchResult(
    TemplateMatchStatus Status,
    int X,
    int Y,
    int Width,
    int Height,
    double Score,
    double SecondScore);

internal static class ZnccMatcher
{
    private const double MinFoundScore = 0.85;
    private const double AmbiguousScoreDelta = 0.05;
    private const double VarianceEpsilon = 1e-6;
    private const double ScaleEpsilon = 1e-12;

    /// <summary>模板长边达到此值且 hay 明显更大时，禁止 ROI 失败后的全帧回退。</summary>
    internal const int FullFrameMinTemplateLongEdge = 64;
    private const int FullFrameHaystackToTemplateRatio = 2;

    public static bool ShouldSkipFullFrameFallback(
        int templateWidth,
        int templateHeight,
        int hayWidth,
        int hayHeight)
    {
        var templateLong = Math.Max(templateWidth, templateHeight);
        if (templateLong < FullFrameMinTemplateLongEdge)
            return false;

        var hayLong = Math.Max(hayWidth, hayHeight);
        return hayLong > templateLong * FullFrameHaystackToTemplateRatio;
    }

    public static TemplateMatchResult Match(
        byte[] haystack,
        int hayWidth,
        int hayHeight,
        int hayStride,
        byte[] template,
        int templateWidth,
        int templateHeight,
        int templateStride,
        double minScale,
        double maxScale,
        CancellationToken cancellationToken = default,
        int searchTimeoutMs = -1)
    {
        ArgumentNullException.ThrowIfNull(haystack);
        ArgumentNullException.ThrowIfNull(template);

        if (cancellationToken.IsCancellationRequested)
            return Empty(TemplateMatchStatus.NotFound);

        var timeoutMs = searchTimeoutMs < 0 ? Limits.V1.ZnccSearchTimeoutMs : searchTimeoutMs;
        using var searchDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeoutMs > 0)
            searchDeadline.CancelAfter(timeoutMs);
        var token = searchDeadline.Token;

        var searchMin = Math.Max(minScale, Limits.V1.TemplateScaleMin);
        var searchMax = Math.Min(maxScale, Limits.V1.TemplateScaleMax);
        if (searchMin > searchMax + ScaleEpsilon)
            return Empty(TemplateMatchStatus.ScaleMismatch);

        if (hayWidth <= 0 || hayHeight <= 0 || templateWidth <= 0 || templateHeight <= 0)
            return Empty(TemplateMatchStatus.NotFound);

        var hay = ToLuma(haystack, hayWidth, hayHeight, hayStride);
        var tmpl = ToLuma(template, templateWidth, templateHeight, templateStride);

        Candidate? best = null;
        Candidate? second = null;

        foreach (var (width, height, pixels) in PyramidTemplates(
                     tmpl, templateWidth, templateHeight, searchMin, searchMax))
        {
            if (token.IsCancellationRequested)
                return Empty(TemplateMatchStatus.NotFound);
            if (width > hayWidth || height > hayHeight)
                continue;

            if (!MatchAtScale(
                    hay, hayWidth, hayHeight, pixels, width, height, token, ref best, ref second))
                return Empty(TemplateMatchStatus.NotFound);
        }

        if (best is null || best.Value.Score < MinFoundScore)
            return ToResult(TemplateMatchStatus.NotFound, best, second);

        var secondScore = second?.Score ?? 0;
        if (best.Value.Score - secondScore <= AmbiguousScoreDelta)
            return ToResult(TemplateMatchStatus.Ambiguous, best, second);

        return ToResult(TemplateMatchStatus.Found, best, second);
    }

    private static bool MatchAtScale(
        float[] hay,
        int hayWidth,
        int hayHeight,
        float[] template,
        int templateWidth,
        int templateHeight,
        CancellationToken cancellationToken,
        ref Candidate? best,
        ref Candidate? second)
    {
        var n = templateWidth * templateHeight;
        double templateSum = 0;
        for (var i = 0; i < template.Length; i++)
            templateSum += template[i];

        var templateMean = templateSum / n;
        var centered = new float[n];
        double templateEnergy = 0;
        for (var i = 0; i < n; i++)
        {
            var v = template[i] - templateMean;
            centered[i] = (float)v;
            templateEnergy += v * v;
        }

        if (templateEnergy < VarianceEpsilon)
            return true;

        var templateNorm = Math.Sqrt(templateEnergy);
        var integralStride = hayWidth + 1;
        var sum = new double[integralStride * (hayHeight + 1)];
        var sumSq = new double[sum.Length];
        BuildIntegrals(hay, hayWidth, hayHeight, sum, sumSq);

        var maxX = hayWidth - templateWidth;
        var maxY = hayHeight - templateHeight;
        for (var y = 0; y <= maxY; y++)
        {
            if (cancellationToken.IsCancellationRequested)
                return false;

            for (var x = 0; x <= maxX; x++)
            {
                var windowSum = RectSum(sum, integralStride, x, y, templateWidth, templateHeight);
                var windowSumSq = RectSum(sumSq, integralStride, x, y, templateWidth, templateHeight);
                var windowVar = windowSumSq - windowSum * windowSum / n;
                if (windowVar < VarianceEpsilon)
                    continue;

                double cross = 0;
                for (var ty = 0; ty < templateHeight; ty++)
                {
                    var hayRow = (y + ty) * hayWidth + x;
                    var tmplRow = ty * templateWidth;
                    for (var tx = 0; tx < templateWidth; tx++)
                        cross += hay[hayRow + tx] * centered[tmplRow + tx];
                }

                var score = cross / (Math.Sqrt(windowVar) * templateNorm);
                Consider(new Candidate(x, y, templateWidth, templateHeight, score), ref best, ref second);
            }
        }

        return true;
    }

    private static void Consider(Candidate current, ref Candidate? best, ref Candidate? second)
    {
        if (double.IsNaN(current.Score))
            return;

        if (best is null)
        {
            best = current;
            return;
        }

        if (IsSamePeak(current, best.Value))
        {
            if (current.Score > best.Value.Score)
                best = current;
            return;
        }

        if (current.Score > best.Value.Score)
        {
            second = best;
            best = current;
            return;
        }

        if (second is null || IsSamePeak(current, second.Value))
        {
            if (second is null || current.Score > second.Value.Score)
                second = current;
            return;
        }

        if (current.Score > second.Value.Score)
            second = current;
    }

    private static bool IsSamePeak(Candidate a, Candidate b)
    {
        var dx = Math.Abs(a.X - b.X);
        var dy = Math.Abs(a.Y - b.Y);
        var sep = Math.Max(2, Math.Min(Math.Min(a.Width, a.Height), Math.Min(b.Width, b.Height)) / 2);
        return Math.Max(dx, dy) < sep;
    }

    private static IEnumerable<(int Width, int Height, float[] Pixels)> PyramidTemplates(
        float[] template,
        int width,
        int height,
        double searchMin,
        double searchMax)
    {
        var seen = new HashSet<(int, int)>();
        foreach (var scale in EnumerateScales(searchMin, searchMax))
        {
            var scaledW = Math.Max(1, (int)Math.Round(width * scale));
            var scaledH = Math.Max(1, (int)Math.Round(height * scale));
            if (!seen.Add((scaledW, scaledH)))
                continue;

            var pixels = scaledW == width && scaledH == height
                ? template
                : ResizeBilinear(template, width, height, scaledW, scaledH);
            yield return (scaledW, scaledH, pixels);
        }
    }

    private static List<double> EnumerateScales(double searchMin, double searchMax)
    {
        var scales = new List<double>();
        AddScale(scales, searchMin, searchMin, searchMax);
        for (var hundredths = 85; hundredths <= 115; hundredths += 5)
            AddScale(scales, hundredths / 100.0, searchMin, searchMax);
        AddScale(scales, searchMax, searchMin, searchMax);
        return scales;
    }

    private static void AddScale(List<double> scales, double scale, double searchMin, double searchMax)
    {
        if (scale + ScaleEpsilon < searchMin || scale - ScaleEpsilon > searchMax)
            return;
        if (scales.Count > 0 && Math.Abs(scales[^1] - scale) <= ScaleEpsilon)
            return;
        scales.Add(scale);
    }

    private static float[] ToLuma(byte[] bgra, int width, int height, int stride)
    {
        var luma = new float[checked(width * height)];
        for (var y = 0; y < height; y++)
        {
            var row = y * stride;
            var dest = y * width;
            for (var x = 0; x < width; x++)
            {
                var i = row + x * 4;
                luma[dest + x] = 0.114f * bgra[i] + 0.587f * bgra[i + 1] + 0.299f * bgra[i + 2];
            }
        }

        return luma;
    }

    private static float[] ResizeBilinear(float[] src, int srcW, int srcH, int dstW, int dstH)
    {
        var dst = new float[checked(dstW * dstH)];
        var scaleX = (double)srcW / dstW;
        var scaleY = (double)srcH / dstH;
        for (var y = 0; y < dstH; y++)
        {
            var fy = (y + 0.5) * scaleY - 0.5;
            var y0 = (int)Math.Floor(fy);
            var y1 = y0 + 1;
            var wy = fy - y0;
            y0 = Math.Clamp(y0, 0, srcH - 1);
            y1 = Math.Clamp(y1, 0, srcH - 1);
            for (var x = 0; x < dstW; x++)
            {
                var fx = (x + 0.5) * scaleX - 0.5;
                var x0 = (int)Math.Floor(fx);
                var x1 = x0 + 1;
                var wx = fx - x0;
                x0 = Math.Clamp(x0, 0, srcW - 1);
                x1 = Math.Clamp(x1, 0, srcW - 1);
                var v00 = src[y0 * srcW + x0];
                var v01 = src[y0 * srcW + x1];
                var v10 = src[y1 * srcW + x0];
                var v11 = src[y1 * srcW + x1];
                var top = v00 + (v01 - v00) * wx;
                var bottom = v10 + (v11 - v10) * wx;
                dst[y * dstW + x] = (float)(top + (bottom - top) * wy);
            }
        }

        return dst;
    }

    private static void BuildIntegrals(float[] hay, int width, int height, double[] sum, double[] sumSq)
    {
        var iw = width + 1;
        for (var y = 0; y < height; y++)
        {
            double rowSum = 0;
            double rowSumSq = 0;
            var hayRow = y * width;
            var intRow = (y + 1) * iw;
            var prevRow = y * iw;
            for (var x = 0; x < width; x++)
            {
                var v = hay[hayRow + x];
                rowSum += v;
                rowSumSq += v * v;
                sum[intRow + x + 1] = sum[prevRow + x + 1] + rowSum;
                sumSq[intRow + x + 1] = sumSq[prevRow + x + 1] + rowSumSq;
            }
        }
    }

    private static double RectSum(double[] integral, int integralStride, int x, int y, int width, int height)
    {
        var left = x;
        var top = y;
        var right = x + width;
        var bottom = y + height;
        return integral[bottom * integralStride + right]
               - integral[top * integralStride + right]
               - integral[bottom * integralStride + left]
               + integral[top * integralStride + left];
    }

    private static TemplateMatchResult Empty(TemplateMatchStatus status)
        => new(status, 0, 0, 0, 0, 0, 0);

    private static TemplateMatchResult ToResult(TemplateMatchStatus status, Candidate? best, Candidate? second)
        => new(
            status,
            best?.X ?? 0,
            best?.Y ?? 0,
            best?.Width ?? 0,
            best?.Height ?? 0,
            best?.Score ?? 0,
            second?.Score ?? 0);

    private readonly record struct Candidate(int X, int Y, int Width, int Height, double Score);
}

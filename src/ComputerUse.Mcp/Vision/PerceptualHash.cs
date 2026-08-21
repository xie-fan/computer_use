using System.Numerics;

namespace ComputerUse.Mcp.Vision;

internal readonly record struct PerceptualHashValue(ulong Bits)
{
    public int HammingDistance(PerceptualHashValue other)
        => BitOperations.PopCount(Bits ^ other.Bits);
}

internal static class PerceptualHash
{
    private const int HashSize = 8;

    public static PerceptualHashValue Compute(byte[] bgra, int width, int height, int stride)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, checked(width * 4));

        Span<int> cells = stackalloc int[HashSize * HashSize];
        DownsampleLuma(bgra, width, height, stride, cells);

        long total = 0;
        for (var i = 0; i < cells.Length; i++)
            total += cells[i];

        ulong bits = 0;
        for (var i = 0; i < cells.Length; i++)
        {
            if (cells[i] * (long)cells.Length > total)
                bits |= 1UL << i;
        }

        return new PerceptualHashValue(bits);
    }

    public static IReadOnlyList<(string Id, int Distance)> Nominate(
        PerceptualHashValue query,
        IReadOnlyList<(string Id, PerceptualHashValue Hash)> library,
        int maxCandidates = 3)
    {
        ArgumentNullException.ThrowIfNull(library);
        if (maxCandidates <= 0 || library.Count == 0)
            return Array.Empty<(string Id, int Distance)>();

        var scored = new (string Id, int Distance)[library.Count];
        for (var i = 0; i < library.Count; i++)
        {
            var item = library[i];
            scored[i] = (item.Id, query.HammingDistance(item.Hash));
        }

        Array.Sort(scored, static (a, b) =>
        {
            var cmp = a.Distance.CompareTo(b.Distance);
            return cmp != 0 ? cmp : string.CompareOrdinal(a.Id, b.Id);
        });

        var take = Math.Min(maxCandidates, scored.Length);
        var nominated = new (string Id, int Distance)[take];
        Array.Copy(scored, nominated, take);
        return nominated;
    }

    private static void DownsampleLuma(
        byte[] bgra,
        int width,
        int height,
        int stride,
        Span<int> cells)
    {
        for (var cy = 0; cy < HashSize; cy++)
        {
            var y0 = cy * height / HashSize;
            var y1 = Math.Max((cy + 1) * height / HashSize, y0 + 1);
            if (y1 > height)
            {
                y0 = height - 1;
                y1 = height;
            }

            for (var cx = 0; cx < HashSize; cx++)
            {
                var x0 = cx * width / HashSize;
                var x1 = Math.Max((cx + 1) * width / HashSize, x0 + 1);
                if (x1 > width)
                {
                    x0 = width - 1;
                    x1 = width;
                }

                long sum = 0;
                var count = 0;
                for (var y = y0; y < y1; y++)
                {
                    var row = y * stride;
                    for (var x = x0; x < x1; x++)
                    {
                        var i = row + x * 4;
                        var b = bgra[i];
                        var g = bgra[i + 1];
                        var r = bgra[i + 2];
                        sum += (r * 77) + (g * 150) + (b * 29);
                        count++;
                    }
                }

                cells[cy * HashSize + cx] = (int)(sum / count);
            }
        }
    }
}

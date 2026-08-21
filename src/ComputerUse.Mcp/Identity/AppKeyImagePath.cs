using System.Text;
using ComputerUse.Mcp.Native;

namespace ComputerUse.Mcp.Identity;

/// <summary>
/// 仅用于 AppKey 路径回退。不要改 <see cref="ComputerUse.Mcp.Abstractions.IProcessQuery.TryGetNormalizedImagePath"/>：
/// v1 HostProcessResolver / list_windows 仍需要未剥版本、未强制小写的路径。
/// </summary>
internal static class AppKeyImagePath
{
    public static string? Normalize(string? path) => Normalize(path, TryGetLongPathName);

    public static string? Normalize(string? path, Func<string, string?> expandLongPath)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        ArgumentNullException.ThrowIfNull(expandLongPath);

        var trimmed = path.Trim();
        string expanded;
        try
        {
            expanded = expandLongPath(trimmed) ?? trimmed;
        }
        catch
        {
            expanded = trimmed;
        }

        if (string.IsNullOrWhiteSpace(expanded))
            return null;

        var lowered = expanded.Replace('/', '\\').ToLowerInvariant();
        return StripVersionDirectorySegments(lowered);
    }

    internal static string StripVersionDirectorySegments(string path)
    {
        var root = Path.GetPathRoot(path) ?? "";
        var remainder = root.Length > 0 && path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? path[root.Length..]
            : path;
        var parts = remainder.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return path;

        var kept = new List<string>(parts.Length);
        for (var i = 0; i < parts.Length; i++)
        {
            var isLeaf = i == parts.Length - 1;
            if (!isLeaf && IsVersionSegment(parts[i]))
                continue;
            kept.Add(parts[i]);
        }

        if (kept.Count == 0)
            return path;

        var joined = string.Join('\\', kept);
        if (root.Length == 0)
            return joined;
        return root.EndsWith('\\') ? root + joined : root + '\\' + joined;
    }

    private static bool IsVersionSegment(string segment)
    {
        // 常见版本子目录：1.2 / 1.2.3 / 1.2.3.4 / v1.0.0
        if (segment.Length is < 3 or > 32)
            return false;

        var span = segment.AsSpan();
        if (span[0] is 'v')
            span = span[1..];
        if (span.Length < 3)
            return false;

        var dots = 0;
        var lastWasDot = true;
        foreach (var c in span)
        {
            if (c == '.')
            {
                if (lastWasDot)
                    return false;
                dots++;
                lastWasDot = true;
                continue;
            }

            if (c is < '0' or > '9')
                return false;
            lastWasDot = false;
        }

        return !lastWasDot && dots is >= 1 and <= 4;
    }

    private static string? TryGetLongPathName(string path)
    {
        try
        {
            var buffer = new StringBuilder(260);
            var n = NativeMethods.GetLongPathName(path, buffer, (uint)buffer.Capacity);
            if (n == 0)
                return null;
            if (n >= buffer.Capacity)
            {
                buffer.EnsureCapacity((int)n);
                n = NativeMethods.GetLongPathName(path, buffer, (uint)buffer.Capacity);
                if (n == 0 || n >= buffer.Capacity)
                    return null;
            }

            var result = buffer.ToString();
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
        catch
        {
            return null;
        }
    }
}

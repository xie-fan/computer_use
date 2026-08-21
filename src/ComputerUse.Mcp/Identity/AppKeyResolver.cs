namespace ComputerUse.Mcp.Identity;

internal sealed record AppIdentity(
    string? PackageFamilyName,
    string? SignerSubject,
    string? ProductName,
    string? ProductVersion,
    string? NormalizedImagePath,
    string ClassName)
{
    /// <summary>进程查询到的原始镜像路径，只写诊断，不参与 AppKey。</summary>
    public string? RawImagePath { get; init; }
}

internal sealed record AppKey(string Value, AppIdentity Diagnostics);

internal static class AppKeyResolver
{
    private const char Separator = '|';

    public static AppKey Compute(AppIdentity identity)
        => new(BuildValue(identity), identity);

    public static bool HasStableIdentity(AppIdentity identity)
    {
        if (Present(identity.PackageFamilyName))
            return true;
        if (Present(identity.SignerSubject)
            && Present(identity.ProductName)
            && Present(identity.ProductVersion))
        {
            return true;
        }

        return Present(identity.NormalizedImagePath);
    }

    private static string BuildValue(AppIdentity identity)
    {
        // 碎片化优于静默合并：只把当前优先级明确要求的字段写进键。
        if (Present(identity.PackageFamilyName))
            return Join(identity.PackageFamilyName, identity.ClassName);

        if (Present(identity.SignerSubject)
            && Present(identity.ProductName)
            && Present(identity.ProductVersion))
        {
            return Join(
                identity.SignerSubject,
                identity.ProductName,
                identity.ProductVersion,
                identity.ClassName);
        }

        if (Present(identity.ProductName) && Present(identity.ProductVersion))
        {
            return Join(
                identity.ProductName,
                identity.ProductVersion,
                identity.NormalizedImagePath,
                identity.ClassName);
        }

        return Join(identity.NormalizedImagePath, identity.ClassName);
    }

    private static bool Present(string? value) => !string.IsNullOrWhiteSpace(value);

    private static string Join(params string?[] parts) => string.Join(Separator, parts);
}

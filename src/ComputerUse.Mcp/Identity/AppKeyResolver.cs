namespace ComputerUse.Mcp.Identity;

internal sealed record AppIdentity(
    string? PackageFamilyName,
    string? SignerSubject,
    string? ProductName,
    string? ProductVersion,
    string? NormalizedImagePath,
    string ClassName);

internal sealed record AppKey(string Value, AppIdentity Diagnostics);

internal static class AppKeyResolver
{
    private const char Separator = '|';

    public static AppKey Compute(AppIdentity identity)
        => new(BuildValue(identity), identity);

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

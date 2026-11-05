using IniaAi.Common.Constants;

namespace IniaAi.Domain.StoredKeys;

/// <summary>
/// A web API call header names a stored key with <c>{StoredKey.Name}</c>. The three API key fields
/// that tenants had before stored keys keep their placeholders, <c>{Organization.ParallelApiKey}</c>
/// and the like, which now name the stored key of the same name. The run leaves these
/// placeholders in the block, and only the request fills them in.
/// </summary>
public static class StoredKeyPlaceholders
{
    public static IReadOnlyList<string> FormerTenantKeyNames { get; } =
        ["ParallelApiKey", "PerplexityApiKey", "FmpApiKey"];

    public static string Token(string name)
    {
        return $"{{{SharedInputKeys.STORED_KEY}.{name}}}";
    }

    /// <summary>The stored key that <paramref name="placeholder"/> names, or null when it names none.</summary>
    public static string? KeyName(string placeholder)
    {
        int dot = placeholder.IndexOf('.');

        if (dot <= 0 || dot == placeholder.Length - 1)
        {
            return null;
        }

        string tag = placeholder[..dot];
        string name = placeholder[(dot + 1)..];

        if (tag.Equals(SharedInputKeys.STORED_KEY, ReplacementTags.Comparison))
        {
            return name;
        }

        return tag.Equals(SharedInputKeys.ORGANIZATION, ReplacementTags.Comparison)
            && FormerTenantKeyNames.Contains(name, StringComparer.Ordinal)
                ? name
                : null;
    }
}

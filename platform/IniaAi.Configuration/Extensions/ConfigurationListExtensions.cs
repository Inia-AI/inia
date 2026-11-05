using Microsoft.Extensions.Configuration;

namespace IniaAi.Configuration.Extensions;

public static class ConfigurationListExtensions
{
    /// <summary>
    /// The non-blank entries of the list under <paramref name="key"/>, trimmed. A list set as one
    /// value, such as <c>Cors__AllowedOrigins=https://app.example.com</c>, binds to an empty list,
    /// so it stops startup instead of taking effect as nothing.
    /// </summary>
    public static string[] GetList(this IConfiguration configuration, string key)
    {
        if (!string.IsNullOrEmpty(configuration[key]))
        {
            throw new InvalidOperationException(
                $"{key} is set as one value, which binds to an empty list. Set each entry as an indexed key, e.g. {key.Replace(":", "__")}__0.");
        }

        return [.. (configuration.GetSection(key).Get<string[]>() ?? [])
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .Select(entry => entry.Trim())];
    }
}

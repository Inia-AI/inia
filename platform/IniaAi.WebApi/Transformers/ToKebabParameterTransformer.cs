using System.Text.RegularExpressions;

namespace IniaAi.WebApi.Transformers;

/// <summary>
/// Transforms route parameter values to kebab-case (lowercase with hyphens) for outbound URLs.
/// </summary>
public partial class ToKebabParameterTransformer : IOutboundParameterTransformer
{
    /// <summary>
    /// Returns a compiled regular expression that inserts a hyphen between lowercase and uppercase letter pairs.
    /// </summary>
    /// <returns>A <see cref="Regex"/> instance for splitting words with dashes.</returns>
    [GeneratedRegex("([a-z])([A-Z])")]
    private static partial Regex SplitWordsWithDashesRegex();

    /// <summary>
    /// Transforms the specified route parameter value to kebab-case.
    /// </summary>
    /// <param name="value">The route parameter value to transform.</param>
    /// <returns>The transformed kebab-case string, or an empty string if the input is null or not a string.</returns>
    public string TransformOutbound(object? value)
    {
        return value is string stringValue
            ? SplitWordsWithDashesRegex().Replace(stringValue, "$1-$2").ToLower()
            : string.Empty;
    }
}

using System.Text.RegularExpressions;

namespace IniaAi.Application.Common.Utilities;

/// <summary>
/// Utility class for extracting placeholder values from templates and resolved text.
/// Provides robust extraction that handles complex placeholder patterns.
/// </summary>
public static partial class PlaceholderExtractor
{
    private static readonly Regex _placeholderRegex = GeneratePlaceholderRegex();

    /// <summary>
    /// Extracts placeholder values from template and resolved text.
    /// Handles complex placeholder patterns like {stopTermBlock.stop}, {CorporationAssessmentYear.Test1}, etc.
    /// </summary>
    /// <param name="template">The template string containing placeholders</param>
    /// <param name="resolved">The resolved string where placeholders have been replaced with actual values</param>
    /// <returns>Dictionary mapping placeholder names to their extracted values</returns>
    public static Dictionary<string, string> ExtractPlaceholderValues(string? template, string? resolved)
    {
        if (string.IsNullOrEmpty(template) || string.IsNullOrEmpty(resolved))
        {
            return [];
        }

        // 1. Get all placeholder names
        var placeholderNames = _placeholderRegex.Matches(template)
            .Cast<Match>()
            .Select(m => m.Groups[1].Value)
            .ToList();

        if (placeholderNames.Count == 0)
        {
            return [];
        }

        // 2. Create mapping from original placeholder names to valid regex group names
        var placeholderToGroupMap = new Dictionary<string, string>();
        for (int i = 0; i < placeholderNames.Count; i++)
        {
            string placeholder = placeholderNames[i];
            string groupName = $"group{i}"; // Use simple numeric group names to avoid issues with dots/special chars
            placeholderToGroupMap[placeholder] = groupName;
        }

        // 3. Escape the whole template for safe use in regex
        string escapedTemplate = Regex.Escape(template);

        // 4. Replace each escaped placeholder with named group
        foreach (KeyValuePair<string, string> kvp in placeholderToGroupMap)
        {
            string escapedPlaceholder = Regex.Escape("{" + kvp.Key + "}");
            string replacement = $"(?<{kvp.Value}>.*?)";
            escapedTemplate = escapedTemplate.Replace(escapedPlaceholder, replacement);
        }

        // 5. Match with RegexOptions.Singleline so '.' matches across lines
        Match match = Regex.Match(resolved, "^" + escapedTemplate + "$", RegexOptions.Singleline);

        if (!match.Success)
        {
            return [];
        }

        // 6. Build result dictionary using original placeholder names
        var result = new Dictionary<string, string>();
        foreach (KeyValuePair<string, string> kvp in placeholderToGroupMap)
        {
            string originalPlaceholder = kvp.Key;
            string groupName = kvp.Value;
            string value = match.Groups[groupName].Value;
            result[originalPlaceholder] = value;
        }

        return result;
    }

    [GeneratedRegex(@"\{([^{}]+?)\}", RegexOptions.Compiled)]
    private static partial Regex GeneratePlaceholderRegex();
}

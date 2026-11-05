using System.Text.RegularExpressions;

namespace IniaAi.Common.Text;

public static partial class FactTags
{
    /// <summary>
    /// Removes fact-annotation markup from text: <c>&lt;fact&gt;</c> /
    /// <c>&lt;fact id="…"&gt;</c> marker tags (keeping their inner text) and whole
    /// <c>&lt;fact_source …&gt;…&lt;/fact_source&gt;</c> blocks (dropping their content).
    /// </summary>
    public static string? Strip(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return content;
        }

        return FactMarkupRegex().Replace(content, string.Empty);
    }

    [GeneratedRegex(@"<fact_source[^>]*>.*?</fact_source>|</?fact(\s+[^>]*)?>", RegexOptions.Singleline)]
    private static partial Regex FactMarkupRegex();
}

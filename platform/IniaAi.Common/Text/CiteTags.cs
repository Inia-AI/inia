using System.Text.RegularExpressions;

namespace IniaAi.Common.Text;

/// <summary>
/// The &lt;cite&gt; marks a model asked for citations sometimes writes into its answer text
/// instead of returning them as citations. They are never part of the answer.
/// </summary>
public static partial class CiteTags
{
    /// <summary>Removes &lt;cite …&gt; and &lt;/cite&gt; marks, keeping the text between them.</summary>
    public static string? Strip(string? text)
    {
        return string.IsNullOrEmpty(text) ? text : CiteTagRegex().Replace(text, string.Empty);
    }

    [GeneratedRegex(@"</?cite(?:\s[^>]*)?>", RegexOptions.IgnoreCase)]
    private static partial Regex CiteTagRegex();
}

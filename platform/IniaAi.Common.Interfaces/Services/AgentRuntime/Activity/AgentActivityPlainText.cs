using System.Text.RegularExpressions;

namespace IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;

/// <summary>
/// The agent writes markdown, and a step is one plain line: emphasis, code marks, headings and
/// list bullets go, and a link keeps only its text, so a link target such as an id never shows.
/// A link cut short by truncation loses its unfinished target too.
/// </summary>
public static partial class AgentActivityPlainText
{
    public static string From(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        string plain = LinkRegex().Replace(text, "$1");
        plain = UnfinishedLinkTargetRegex().Replace(plain, string.Empty);
        plain = UnfinishedLinkOpenerRegex().Replace(plain, string.Empty);
        plain = LinePrefixRegex().Replace(plain, string.Empty);
        plain = StrongRegex().Replace(plain, string.Empty);
        plain = EmphasisRegex().Replace(plain, string.Empty);
        return plain.Replace("`", string.Empty, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\[([^\]\n]*)\]\([^)\n]*\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"\]\([^)\n]*$", RegexOptions.Multiline)]
    private static partial Regex UnfinishedLinkTargetRegex();

    [GeneratedRegex(@"\[(?=[^\]\n]*$)", RegexOptions.Multiline)]
    private static partial Regex UnfinishedLinkOpenerRegex();

    [GeneratedRegex(@"^\s*(?:#{1,6}\s+|[-*+]\s+|>\s*)", RegexOptions.Multiline)]
    private static partial Regex LinePrefixRegex();

    [GeneratedRegex(@"\*\*|__")]
    private static partial Regex StrongRegex();

    [GeneratedRegex(@"(?<![\w*])\*(?=\S)|(?<=\S)\*(?![\w*])")]
    private static partial Regex EmphasisRegex();
}

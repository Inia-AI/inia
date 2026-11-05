using IniaAi.Core.Services;
using System.Text;
using System.Text.RegularExpressions;

namespace IniaAi.Core.Workflows.Agents;

/// <summary>
/// Renders agent worker skills under the strict double-brace grammar: one left-to-right walk,
/// resolving only <c>{{tag}}</c> occurrences and splicing each substituted value literally —
/// the value is never re-scanned, so tags or braces inside it stay text instead of becoming an
/// injection channel. Single-brace <c>{...}</c> text is structure and passes through verbatim.
/// Deliberately NOT StringExtensions.ReplacePlaceholders (dual syntax, forward-chains through
/// the evolving string) and NOT the transformed-values walk (deliberate nested resolution).
/// </summary>
public static partial class AgentTemplateRenderer
{
    public sealed record AgentRenderResult(string Content, IReadOnlyList<string> Warnings);

    // The tag character class matches the engine's placeholder grammar
    // (StringExtensions.ANCHORED_PLACEHOLDER); only its double-brace arm exists here.
    [GeneratedRegex(@"\G\{\{(?<tag>[a-zA-Z0-9._\-\[\] &/()%]+)\}\}")]
    private static partial Regex DoubleBracePlaceholder();

    public static AgentRenderResult Render(string template, IReadOnlyDictionary<string, string?> inputs)
    {
        var content = new StringBuilder(template.Length);
        List<string> warnings = [];
        int index = 0;

        while (index < template.Length)
        {
            char current = template[index];
            if (current != '{')
            {
                _ = content.Append(current);
                index++;
                continue;
            }

            Match match = DoubleBracePlaceholder().Match(template, index);
            if (!match.Success)
            {
                _ = content.Append(current);
                index++;
                continue;
            }

            string tag = match.Groups["tag"].Value;
            if (!inputs.TryGetValue(tag, out string? value))
            {
                warnings.Add($"Unknown tag '{{{{{tag}}}}}' left verbatim");
                _ = content.Append(match.Value);
            }
            else if (string.IsNullOrWhiteSpace(value)
                || string.Equals(value, JsonStringReplacerStrategy.DefaultNoValue, StringComparison.Ordinal))
            {
                warnings.Add($"Tag '{{{{{tag}}}}}' has no usable value and stays verbatim");
                _ = content.Append(match.Value);
            }
            else
            {
                _ = content.Append(value);
            }

            index += match.Length;
        }

        return new AgentRenderResult(content.ToString(), warnings);
    }
}

using IniaAi.Common;
using System.Text.RegularExpressions;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties;

/// <summary>
/// Engine-tag scan for the agent-template grammar. Worker-skill content is ignore-flagged for
/// the reflection placeholder machinery, so this scan is the single seam that makes skill
/// references visible to the engine — graph edges, dispatch inputs, and save-time checks all
/// consume it through <see cref="AgentProperties.GetAllPlaceholders"/>. Deliberately strict:
/// double-brace only (single-brace JSON/prose stays inert), identifier-shaped tags only (no
/// spaces, so Jinja/Helm-style <c>{{ var }}</c> stays inert), and never <c>$</c>-prefixed
/// (GitHub-Actions <c>${{ … }}</c> stays inert). Strategy-supplied namespaces
/// (<c>Validator.*</c>, <c>Verifier.*</c>) are excluded — dispatch would otherwise stamp them
/// as NoData inputs the strategy loop never gets to supply.
/// </summary>
public static partial class AgentTemplateTags
{
    public const string VALIDATOR_NAMESPACE = "Validator";
    public const string VERIFIER_NAMESPACE = "Verifier";

    public static IEnumerable<string> ExtractEngineTags(string? skillContent)
    {
        if (string.IsNullOrWhiteSpace(skillContent))
        {
            yield break;
        }

        HashSet<string> seen = [];

        foreach (Match match in ENGINE_TAG().Matches(skillContent))
        {
            string tag = match.Groups["tag"].Value;

            if (!IsStrategyNamespace(tag) && seen.Add(tag))
            {
                yield return tag;
            }
        }
    }

    /// <summary>
    /// Rewrites each double-brace engine tag in <paramref name="text"/> through a rewrite written for
    /// the single-brace grammar, so loop and parameterized expansion reach agent templates with the
    /// same rewrite they apply to every other field. The tag is handed over as <c>{tag}</c>; every
    /// tag-shaped single-brace token in the result goes back in double braces, and any other text
    /// (a parameter value) is spliced as written. Text outside double braces is never touched.
    /// </summary>
    public static string TransformEngineTags(string? text, Func<string, string> transform)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("{{", StringComparison.Ordinal))
        {
            return text ?? string.Empty;
        }

        return ENGINE_TAG().Replace(text, match =>
        {
            string tag = match.Groups["tag"].Value;
            if (IsStrategyNamespace(tag))
            {
                return match.Value;
            }

            string singleBrace = "{" + tag + "}";
            string rewritten = transform(singleBrace);

            return string.Equals(rewritten, singleBrace, StringComparison.Ordinal)
                ? match.Value
                : SINGLE_BRACE_TAG().Replace(rewritten, static token => "{" + token.Value + "}");
        });
    }

    public static bool IsStrategyNamespace(string tag)
    {
        return tag.GetBaseReplacementTag() is VALIDATOR_NAMESPACE or VERIFIER_NAMESPACE;
    }

    [GeneratedRegex(@"(?<!\$)\{\{(?<tag>[A-Za-z0-9_][A-Za-z0-9._\-\[\]]*)\}\}")]
    private static partial Regex ENGINE_TAG();

    // A token that starts with a digit (a regex quantifier, a format slot) is text, never a tag.
    [GeneratedRegex(@"(?<!\{)\{[A-Za-z_][A-Za-z0-9._\-\[\]]*\}(?!\})")]
    private static partial Regex SINGLE_BRACE_TAG();
}

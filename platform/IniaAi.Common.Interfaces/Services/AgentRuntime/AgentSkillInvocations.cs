using System.Text.RegularExpressions;

namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// What counts as a skill invocation in an agent block's prompt text, and whether it resolves.
/// The rule mirrors the runtime exactly: skill files register under their normalized identity
/// inside the runtime's <c>skills</c> plugin, so the only invocation that resolves at run time
/// is <c>/skills:&lt;identity&gt;</c> with the identity spelled exactly, and only against the
/// same block's own skill set. An invocation that misses resolves to nothing — the CLI answers
/// "Unknown command" host-side and the prompt never reaches the model — which is why every
/// prefixed occurrence is checked. Text without the prefix is ordinary prose (paths like
/// <c>/docs/report.pdf</c> must never be flagged); the one exception is a bare <c>/name</c>
/// that maps onto one of the block's skills, where the intent is unambiguous and the author is
/// pointed at the prefixed form instead of being silently second-guessed.
/// </summary>
public static partial class AgentSkillInvocations
{
    public const string INVOCATION_PREFIX = "/skills:";

    private const string PREFIX_IDENTITY = "skills";
    private const string SUBSTITUTION_TAG_OPENER = "{{";
    private static readonly char[] PLACEHOLDER_OPENERS = ['<', '[', '{'];

    public enum ProblemKind
    {
        UnknownName,
        MissingName,
        BareForm,
    }

    /// <summary>
    /// One problem per distinct written token. <see cref="ResolvableIdentity"/> carries the
    /// block skill the token maps onto once normalized — the "did you mean" the caller can
    /// offer — and is always present on a <see cref="ProblemKind.BareForm"/> problem, which
    /// exists only when the bare name maps onto a block skill.
    /// </summary>
    public sealed record InvocationProblem(ProblemKind Kind, string Token, string? ResolvableIdentity);

    public static List<InvocationProblem> FindProblems(string? text, IReadOnlySet<string> skillIdentities)
    {
        var problems = new List<InvocationProblem>();
        if (string.IsNullOrEmpty(text))
        {
            return problems;
        }

        var reported = new HashSet<InvocationProblem>();

        foreach (Match match in PrefixedInvocationPattern().Matches(text))
        {
            string prefix = match.Groups["prefix"].Value;
            string rawName = match.Groups["name"].Value;

            if (IsUncheckableName(rawName))
            {
                continue;
            }

            string name = TrimTrailingProsePunctuation(rawName);

            if (name.Length == 0)
            {
                AddOnce(problems, reported, new InvocationProblem(
                    ProblemKind.MissingName, INVOCATION_PREFIX, ResolvableIdentity: null));
                continue;
            }

            if (string.Equals(prefix, PREFIX_IDENTITY, StringComparison.Ordinal)
                && skillIdentities.Contains(name))
            {
                continue;
            }

            AddOnce(problems, reported, new InvocationProblem(
                ProblemKind.UnknownName, $"/{prefix}:{name}", ResolvableIdentityOf(name, skillIdentities)));
        }

        foreach (Match match in BareTokenPattern().Matches(text))
        {
            string name = TrimTrailingProsePunctuation(match.Groups["name"].Value);

            if (name.Length > 0 && ResolvableIdentityOf(name, skillIdentities) is string identity)
            {
                AddOnce(problems, reported, new InvocationProblem(ProblemKind.BareForm, $"/{name}", identity));
            }
        }

        return problems;
    }

    // Two shapes of name cannot be judged at save time, and an unjudgeable name must never
    // block a save: one carrying a substitution tag ({{tag}}), whose value exists only at
    // execution — the runtime's zero-turn terminal guard catches a bad expansion at
    // $0 — and one that is a placeholder (<name>, [skill-name], {identity}), which is how
    // prose documents the convention rather than invokes it.
    private static bool IsUncheckableName(string rawName)
    {
        return rawName.Contains(SUBSTITUTION_TAG_OPENER, StringComparison.Ordinal)
            || (rawName.Length > 0 && PLACEHOLDER_OPENERS.Contains(rawName[0]));
    }

    private static string? ResolvableIdentityOf(string name, IReadOnlySet<string> skillIdentities)
    {
        return AgentSkillNames.Normalize(name) is string identity && skillIdentities.Contains(identity)
            ? identity
            : null;
    }

    private static void AddOnce(
        List<InvocationProblem> problems,
        HashSet<InvocationProblem> reported,
        InvocationProblem problem)
    {
        if (reported.Add(problem))
        {
            problems.Add(problem);
        }
    }

    private static string TrimTrailingProsePunctuation(string name)
    {
        return TrailingProsePunctuationPattern().Replace(name, string.Empty);
    }

    [GeneratedRegex(@"(?<![\w/])/(?<prefix>(?i:skills)):(?<name>\S*)")]
    private static partial Regex PrefixedInvocationPattern();

    // The trailing lookahead excludes every character the name charset itself contains (plus
    // ':' and '/'), so backtracking can never shorten a match into a false hit — '/docs/x'
    // stays a path instead of degrading to a match on '/doc'.
    [GeneratedRegex(@"(?<![\w/:])/(?<name>[A-Za-z0-9][A-Za-z0-9._-]*)(?![\w.:/-])")]
    private static partial Regex BareTokenPattern();

    // Includes the markdown-emphasis and bracket closers (*, _, }, >) so prose like
    // **/skills:extraction** or {/skills:extraction} resolves on the name the author meant.
    [GeneratedRegex("[),.;:!?\\]'\"`*_}>]+\\z")]
    private static partial Regex TrailingProsePunctuationPattern();
}

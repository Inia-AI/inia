using System.Text.RegularExpressions;

namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

public static partial class AgentSkillNames
{
    public const int MAX_LENGTH = 64;

    public const int MAX_DESCRIPTION_LENGTH = 1400;

    public static string? Normalize(string? rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return null;
        }

        string stripped = MarkdownExtensionPattern().Replace(rawName, string.Empty).ToLowerInvariant();
        string hyphenated = HyphenEdgesPattern().Replace(NonIdentityCharsPattern().Replace(stripped, "-"), string.Empty);
        string cut = hyphenated.Length <= MAX_LENGTH ? hyphenated : hyphenated[..MAX_LENGTH];
        string normalized = HyphenEdgesPattern().Replace(cut, string.Empty);

        return NormalizedPattern().IsMatch(normalized) ? normalized : null;
    }

    /// <summary>
    /// The runtime's admission rules for a skill set, in one place so the block strategy can refuse
    /// before it spends anything and the client can refuse before it uploads anything. Returns the
    /// first problem as a message, or null when the set is admissible.
    /// </summary>
    public static string? DescribeFirstProblem(IEnumerable<(string Name, string Description)> skills)
    {
        var identities = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach ((string name, string description) in skills)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                return $"Skill '{name}' has no description. A skill without one is not registered "
                    + "with the model, and the run fails skill_register_failed.";
            }

            if (description.Length > MAX_DESCRIPTION_LENGTH)
            {
                return $"Skill '{name}' has a description of {description.Length} characters, "
                    + $"over the runtime's {MAX_DESCRIPTION_LENGTH} character cap.";
            }

            if (Normalize(name) is not string identity)
            {
                return $"Skill name '{name}' keeps no skill identity once normalized.";
            }

            if (!string.Equals(identity, name, StringComparison.Ordinal))
            {
                return $"Skill name '{name}' is not a skill identity. Use lower-case words joined by "
                    + $"single hyphens, at most {MAX_LENGTH} characters and no file extension — "
                    + $"'{identity}', for instance.";
            }

            if (identities.TryGetValue(identity, out string? priorName))
            {
                return $"Skill names '{priorName}' and '{name}' both normalize to the identity '{identity}'.";
            }

            identities.Add(identity, name);
        }

        return null;
    }

    [GeneratedRegex(@"\.(?:md|markdown)\z", RegexOptions.IgnoreCase)]
    private static partial Regex MarkdownExtensionPattern();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonIdentityCharsPattern();

    [GeneratedRegex(@"\A-+|-+\z")]
    private static partial Regex HyphenEdgesPattern();

    [GeneratedRegex(@"\A[a-z0-9]+(?:-[a-z0-9]+)*\z")]
    private static partial Regex NormalizedPattern();
}

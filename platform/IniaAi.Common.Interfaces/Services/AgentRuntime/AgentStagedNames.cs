namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// Repairs document names onto the runtime's staged-input rules: names are admitted against
/// <c>^[A-Za-z0-9._ -]{1,200}$</c>, the skills manifest name is reserved, and documents and
/// skill files share one namespace. User-uploaded document names ("Invoice (1).pdf",
/// non-ASCII) routinely violate that, so they are mapped onto the admitted alphabet instead
/// of failing the run. Skill names are not repaired here — they are an invocation identity with
/// its own alphabet and its own repair path (<see cref="AgentSkillNames"/>). The client validates,
/// this helper repairs, and both live beside the contract rules they satisfy.
/// </summary>
public static class AgentStagedNames
{
    public const int MAX_LENGTH = 200;

    private const string FALLBACK_DOCUMENT_NAME = "document";
    private const string RESERVED_NAME_PREFIX = "doc-";

    private static readonly string[] _reservedNames = [".", "..", "manifest.json"];

    /// <summary>
    /// Maps one document name onto the admitted alphabet, unique against
    /// <paramref name="usedNames"/> — collisions get an extension-preserving <c>_2</c>,
    /// <c>_3</c>, … suffix. The returned name is added to <paramref name="usedNames"/>.
    /// </summary>
    public static string Stage(string? documentName, HashSet<string> usedNames)
    {
        string sanitized = Sanitize(documentName);
        if (usedNames.Add(sanitized))
        {
            return sanitized;
        }

        (string stem, string extension) = SplitExtension(sanitized);

        for (int copy = 2; ; copy++)
        {
            string suffix = $"_{copy}";
            int stemBudget = Math.Max(1, MAX_LENGTH - suffix.Length - extension.Length);
            string candidate = (stem.Length > stemBudget ? stem[..stemBudget] : stem) + suffix + extension;
            if (usedNames.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private static string Sanitize(string? documentName)
    {
        string trimmed = (documentName ?? string.Empty).Trim();
        string sanitized = string.Concat(trimmed.Select(static c => IsStagedNameChar(c) ? c : '_'));

        if (sanitized.Length == 0)
        {
            sanitized = FALLBACK_DOCUMENT_NAME;
        }

        if (_reservedNames.Contains(sanitized, StringComparer.Ordinal))
        {
            sanitized = RESERVED_NAME_PREFIX + sanitized;
        }

        return sanitized.Length <= MAX_LENGTH ? sanitized : TruncatePreservingExtension(sanitized);
    }

    private static string TruncatePreservingExtension(string sanitized)
    {
        (string stem, string extension) = SplitExtension(sanitized);
        int stemBudget = MAX_LENGTH - extension.Length;
        if (extension.Length == 0 || stemBudget < 1)
        {
            return sanitized[..MAX_LENGTH];
        }

        return stem[..stemBudget] + extension;
    }

    /// <summary>
    /// A leading dot is a dotfile, not an extension — mirrored by the runtime's staged-name
    /// rules and the dedup suffixing, which both leave dotfiles whole.
    /// </summary>
    private static (string Stem, string Extension) SplitExtension(string name)
    {
        int extensionIndex = name.LastIndexOf('.');
        return extensionIndex > 0
            ? (name[..extensionIndex], name[extensionIndex..])
            : (name, string.Empty);
    }

    private static bool IsStagedNameChar(char c)
    {
        return char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or ' ' or '-';
    }
}

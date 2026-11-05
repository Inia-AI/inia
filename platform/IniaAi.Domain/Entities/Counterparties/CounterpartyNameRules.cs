namespace IniaAi.Domain.Entities.Counterparties;

/// <summary>
/// Content rules for counterparty names and name variants. Counterparty names are interpolated
/// into workflow prompts and raw JSON request payloads (e.g. WebApiCall blocks), so characters
/// that can break JSON strings or template placeholders are not allowed. Apostrophes are allowed
/// because they legitimately occur in company names (e.g. O'Reilly). Pipes are allowed in name
/// variants only, where they act as the separator between variants (e.g. "ibm corp|int.bus.m").
/// </summary>
public static class CounterpartyNameRules
{
    public const char NAME_VARIANTS_SEPARATOR = '|';

    public const string FORBIDDEN_CHARACTERS = "\"“”„«»`\\{}|<>";

    public const string NAME_VARIANTS_FORBIDDEN_CHARACTERS = "\"“”„«»`\\{}<>";

    public const string FORBIDDEN_CHARACTERS_MESSAGE =
        "must not contain double quotes, backslashes, braces, backticks, pipes, or angle brackets (apostrophes are allowed)";

    public const string NAME_VARIANTS_FORBIDDEN_CHARACTERS_MESSAGE =
        "must not contain double quotes, backslashes, braces, backticks, or angle brackets (apostrophes and pipe separators are allowed)";

    /// <summary>
    /// Returns true when a counterparty name contains no forbidden or control characters.
    /// Null or empty values are considered valid; presence is enforced separately.
    /// </summary>
    public static bool IsValid(string? value)
    {
        return ContainsNoForbiddenCharacters(value, FORBIDDEN_CHARACTERS);
    }

    /// <summary>
    /// Returns true when a name-variants value contains no forbidden or control characters.
    /// Unlike <see cref="IsValid"/>, the pipe character is allowed because it separates variants.
    /// </summary>
    public static bool IsValidNameVariants(string? value)
    {
        return ContainsNoForbiddenCharacters(value, NAME_VARIANTS_FORBIDDEN_CHARACTERS);
    }

    /// <summary>
    /// Returns each variant a name-variants value holds, trimmed, without empty entries.
    /// </summary>
    public static string[] SplitNameVariants(string? nameVariants)
    {
        return string.IsNullOrWhiteSpace(nameVariants)
            ? []
            : nameVariants.Split(
                NAME_VARIANTS_SEPARATOR,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool ContainsNoForbiddenCharacters(string? value, string forbiddenCharacters)
    {
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        foreach (char c in value)
        {
            if (char.IsControl(c) || forbiddenCharacters.Contains(c))
            {
                return false;
            }
        }

        return true;
    }
}

using System.Text.RegularExpressions;

namespace IniaAi.Domain.StoredKeys;

public static partial class StoredKeyNames
{
    public const int MAX_LENGTH = 100;
    public const int VALUE_MAX_LENGTH = 1000;

    public const string RULE = "Use letters, digits, dashes and underscores, starting with a letter.";

    public static bool IsValid(string? name)
    {
        return !string.IsNullOrEmpty(name) && name.Length <= MAX_LENGTH && NamePattern().IsMatch(name);
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]*$")]
    private static partial Regex NamePattern();
}

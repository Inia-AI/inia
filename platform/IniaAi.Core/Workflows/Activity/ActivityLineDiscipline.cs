namespace IniaAi.Core.Workflows.Activity;

/// <summary>
/// The one-clause line discipline every activity surface shares: the prompts ask the cheap
/// model for one clause of at most 80 characters, measured output breaks that (up to 139
/// characters), and rejecting an over-long line would surface something worse — so an
/// over-long line is trimmed to its first clause when that carries enough on its own,
/// otherwise to a word boundary. A model refusal is never a feed line.
/// </summary>
public static class ActivityLineDiscipline
{
    public const int ONE_CLAUSE_MAX_CHARS = 80;
    public const int ONE_CLAUSE_MIN_CHARS = 40;

    private static readonly string[] _refusalPrefixes =
        ["I can't", "I cannot", "I'm sorry", "I am sorry", "Sorry,"];

    public static string TrimToOneClause(string display)
    {
        if (display.Length <= ONE_CLAUSE_MAX_CHARS)
        {
            return display;
        }

        int comma = display.IndexOf(',', StringComparison.Ordinal);
        if (comma >= ONE_CLAUSE_MIN_CHARS && comma <= ONE_CLAUSE_MAX_CHARS)
        {
            return display[..comma];
        }

        int space = display.LastIndexOf(' ', ONE_CLAUSE_MAX_CHARS - 1);
        return space >= ONE_CLAUSE_MIN_CHARS
            ? string.Concat(display.AsSpan(0, space), "…")
            : display[..ONE_CLAUSE_MAX_CHARS];
    }

    public static bool LooksLikeRefusal(string display)
    {
        return _refusalPrefixes.Any(prefix =>
            display.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}

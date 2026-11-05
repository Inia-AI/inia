using System.Text.RegularExpressions;

namespace IniaAi.Core.Workflows.Annotations;

/// <summary>
/// The words around a figure, so two texts that state the same value can be told apart by what
/// they say about it: £1.2m of overdraft is not £1.2m of revenue. Short words and filler carry no
/// meaning and are left out.
/// </summary>
internal static partial class ClaimContext
{
    /// <summary>
    /// Characters each side of a figure whose words count as its context, wide enough to hold the
    /// clause around the figure in prose and the row label in a table.
    /// </summary>
    public const int CLAUSE_CHARS = 160;

    /// <summary>
    /// The narrower context a bare year gets. A whole page cited as one excerpt states a year in
    /// many places, and a wide window around each would hold most of the page's words.
    /// </summary>
    public const int NEAR_CHARS = 60;

    private const int MIN_WORD_LENGTH = 4;

    private static readonly HashSet<string> _fillerWords =
    [
        "that", "this", "with", "from", "into", "have", "been", "were", "which", "their", "there", "about",
        "after", "before", "while", "would", "could", "should", "other", "than", "also", "more", "less",
        "over", "under", "only", "very", "such", "each", "both", "onto", "year", "years",
    ];

    public static HashSet<string> WordsAround(string text, int index, int length, int contextChars = CLAUSE_CHARS)
    {
        int start = Math.Max(0, index - contextChars);
        int end = Math.Min(text.Length, index + length + contextChars);
        return Words(text[start..end]);
    }

    /// <summary>
    /// The words around every place <paramref name="text"/> states <paramref name="value"/> as its
    /// own token, or none when it never does: the claim, read wherever it is made.
    /// </summary>
    public static HashSet<string> WordsAroundValue(string text, string value, int contextChars = CLAUSE_CHARS)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);

        foreach ((int Index, int Length) match in NumericalGrounding.MatchesOnNumberBoundaries(text, value))
        {
            words.UnionWith(WordsAround(text, match.Index, match.Length, contextChars));
        }

        return words;
    }

    /// <summary>
    /// The most words that any one place <paramref name="text"/> states <paramref name="value"/>
    /// shares with the claim. Each place is scored on its own, so a long source does not win by
    /// holding more words.
    /// </summary>
    public static int SharedWords(HashSet<string> claimWords, string text, string value, int contextChars = CLAUSE_CHARS)
    {
        return NumericalGrounding.MatchesOnNumberBoundaries(text, value)
            .Select(match => SharedWords(claimWords, text, match.Index, match.Length, contextChars))
            .DefaultIfEmpty(0)
            .Max();
    }

    public static int SharedWords(HashSet<string> claimWords, string text, int index, int length, int contextChars = CLAUSE_CHARS)
    {
        return WordsAround(text, index, length, contextChars).Count(claimWords.Contains);
    }

    /// <summary>
    /// Whether <paramref name="source"/> says about <paramref name="value"/> what
    /// <paramref name="claim"/> says about it: the words near the value in each share at least
    /// one. Used for a bare year, which says nothing on its own.
    /// </summary>
    public static bool SpeaksOfTheSameThing(string claim, string source, string value)
    {
        return SpeaksOfTheSameThing(WordsNearValue(claim, value), source, value);
    }

    /// <summary>The same test with the claim's words near the value already read.</summary>
    public static bool SpeaksOfTheSameThing(HashSet<string> claimWords, string source, string value)
    {
        return claimWords.Count > 0 && SharedWords(claimWords, source, value, NEAR_CHARS) > 0;
    }

    public static HashSet<string> WordsNearValue(string text, string value)
    {
        return WordsAroundValue(text, value, NEAR_CHARS);
    }

    /// <summary>The items with the highest score, every one of them on a tie.</summary>
    public static List<T> TopScoring<T>(IEnumerable<T> items, Func<T, int> score)
    {
        List<(T Item, int Score)> scored = [.. items.Select(item => (item, score(item)))];
        if (scored.Count == 0)
        {
            return [];
        }

        int best = scored.Max(candidate => candidate.Score);
        return [.. scored.Where(candidate => candidate.Score == best).Select(candidate => candidate.Item)];
    }

    private static HashSet<string> Words(string text)
    {
        return [.. WordRegex().Matches(text)
            .Select(match => match.Value.ToLowerInvariant())
            .Where(word => word.Length >= MIN_WORD_LENGTH && !_fillerWords.Contains(word))];
    }

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex WordRegex();
}

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using IniaAi.Domain.Proposals;
using Newtonsoft.Json.Linq;

namespace IniaAi.Application.Workflows.EditorAgent;

/// <summary>
/// Keeps one organization's report data out of the template every organization shares. A
/// proposed change is caught when its new text holds a passage of at least
/// <see cref="MIN_COPIED_CHARS"/> characters from a staged report, the name of a staged report's
/// client or document, or a confidential variable's value, and that text was not already in the workflow. A proposal is also caught
/// when its changes bring in <see cref="MIN_COPIED_FIGURES"/> or more figures that a report holds
/// and the workflow does not. Text is compared lowercased with whitespace collapsed, so re-wrapping
/// a passage does not hide it. Passages are found with a rolling hash, so the cost is linear in the
/// report text. Built once per turn; reports the agent opens later in the turn are added to it.
/// </summary>
public sealed partial class EditorAgentReportLeaks
{
    public const int MIN_COPIED_CHARS = 40;
    public const int MIN_NAME_CHARS = 4;
    public const int MIN_CONFIDENTIAL_CHARS = 3;
    public const int MIN_COPIED_FIGURES = 3;

    private const ulong HASH_BASE = 1_000_003;
    private const string NO_COUNTERPARTY = "no counterparty";
    private const string PASSAGE = "a passage from a past report";
    private const string NAME = "a client or document name from a past report";
    private const string FIGURES = "figures from a past report";
    private const int MIN_YEAR = 1900;
    private const int MAX_YEAR = 2199;
    private const int MIN_PLAIN_DIGITS = 3;

    private static readonly ulong _dropFactor = Power(HASH_BASE, MIN_COPIED_CHARS - 1);

    private readonly List<string> _texts = [];
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);
    private readonly HashSet<string> _figures = new(StringComparer.Ordinal);

    public EditorAgentReportLeaks(IEnumerable<EditorAgentReportSummary> listed)
    {
        foreach (EditorAgentReportSummary report in listed)
        {
            AddNames(report);
        }
    }

    public EditorAgentReportLeaks(IEnumerable<EditorAgentReportEvidence> evidence, IEnumerable<EditorAgentReportSummary> listed)
        : this(listed)
    {
        foreach (EditorAgentReportEvidence report in evidence)
        {
            Add(report);
        }
    }

    public bool IsEmpty => _texts.Count == 0 && _names.Count == 0 && _figures.Count == 0;

    /// <summary>A staged report: its run's text, its documents' text, its names and its confidential values.</summary>
    public void Add(EditorAgentReportEvidence evidence)
    {
        AddNames(evidence.Summary);
        AddTexts(evidence.RunTexts);
        foreach (string value in evidence.ConfidentialValues)
        {
            string normalized = Normalize(value);
            if (normalized.Length >= MIN_CONFIDENTIAL_CHARS)
            {
                _ = _names.Add(normalized);
            }
        }
    }

    /// <summary>Text that counts as report text.</summary>
    public void AddTexts(IEnumerable<string> texts)
    {
        foreach (string text in texts)
        {
            string normalized = Normalize(text);
            _figures.UnionWith(Figures(normalized));
            if (normalized.Length >= MIN_COPIED_CHARS)
            {
                _texts.Add(normalized);
            }
        }
    }

    public IReadOnlyList<(ProposedChange Change, string What)> Find(IReadOnlyList<ProposedChange> changes, IEnumerable<string> workflowTexts)
    {
        return Find(changes, new EditorAgentWorkflowText(workflowTexts));
    }

    /// <summary>
    /// The changes whose new text holds report text the workflow did not already hold, with what
    /// they hold in plain words.
    /// </summary>
    public IReadOnlyList<(ProposedChange Change, string What)> Find(IReadOnlyList<ProposedChange> changes, EditorAgentWorkflowText workflow)
    {
        var found = new List<(ProposedChange, string)>();
        if (IsEmpty)
        {
            return found;
        }

        var windows = new Dictionary<ulong, List<(int Change, string Window)>>();
        var newTexts = new List<string>();
        var beforeTexts = new List<string>();
        for (int index = 0; index < changes.Count; index++)
        {
            string text = Normalize(string.Join('\n', StringsOf(changes[index].NewJson)));
            string before = Normalize(string.Join('\n', StringsOf(changes[index].BaseJson)));
            newTexts.Add(text);
            beforeTexts.Add(before);
            foreach ((ulong hash, int start) in Windows(text))
            {
                string window = text.Substring(start, MIN_COPIED_CHARS);
                if (before.Contains(window, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!windows.TryGetValue(hash, out List<(int, string)>? list))
                {
                    list = [];
                    windows[hash] = list;
                }

                list.Add((index, window));
            }
        }

        HashSet<string> inWorkflow = Matching(workflow.Texts, windows);
        HashSet<string> inReports = Matching(_texts, windows);
        var copied = new HashSet<int>(windows.Values
            .SelectMany(static list => list)
            .Where(entry => inReports.Contains(entry.Window) && !inWorkflow.Contains(entry.Window))
            .Select(static entry => entry.Change));

        List<HashSet<string>> introduced = [.. newTexts.Select((text, index) =>
        {
            HashSet<string> figures = Figures(text);
            figures.ExceptWith(Figures(beforeTexts[index]));
            figures.ExceptWith(workflow.Figures);
            figures.IntersectWith(_figures);
            return figures;
        })];
        bool tooManyFigures = introduced.SelectMany(static figures => figures).Distinct(StringComparer.Ordinal).Count() >= MIN_COPIED_FIGURES;

        for (int index = 0; index < changes.Count; index++)
        {
            if (copied.Contains(index))
            {
                found.Add((changes[index], PASSAGE));
                continue;
            }

            string text = newTexts[index];
            string before = beforeTexts[index];
            if (_names.FirstOrDefault(name => ContainsWord(text, name) && !ContainsWord(before, name)
                && !workflow.Texts.Any(value => ContainsWord(value, name))) is not null)
            {
                found.Add((changes[index], NAME));
                continue;
            }

            if (tooManyFigures && introduced[index].Count > 0)
            {
                found.Add((changes[index], FIGURES));
            }
        }

        return found;
    }

    /// <summary>
    /// The figures in normalized text: a number with a decimal point, a thousands separator, a
    /// percent sign or a scale (1,234 / 5.6% / 12.3m), or a whole number of at least three digits
    /// that is not a year. Each is keyed by its digits, so 1,234.5 and 1234.5 are one figure.
    /// </summary>
    public static HashSet<string> Figures(string normalized)
    {
        var figures = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in FigurePattern().Matches(normalized))
        {
            string digits = match.Groups["number"].Value;
            bool grouped = digits.Contains(',', StringComparison.Ordinal);
            bool fractional = digits.Contains('.', StringComparison.Ordinal);
            bool scaled = match.Groups["scale"].Success;
            string key = digits.Replace(",", string.Empty, StringComparison.Ordinal);
            if (!grouped && !fractional && !scaled && (key.Length < MIN_PLAIN_DIGITS || IsYear(key)))
            {
                continue;
            }

            _ = figures.Add(key);
        }

        return figures;
    }

    [GeneratedRegex(@"(?<![\w.,])(?<number>\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)(?:\s?(?<scale>%|bn|m|k|million|billion|thousand)(?![a-z]))?")]
    private static partial Regex FigurePattern();

    private static bool IsYear(string digits)
    {
        return digits.Length == 4
            && int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int year)
            && year is >= MIN_YEAR and <= MAX_YEAR;
    }

    /// <summary>The windows of the changes that also occur somewhere in <paramref name="sources"/>.</summary>
    private static HashSet<string> Matching(IEnumerable<string> sources, Dictionary<ulong, List<(int Change, string Window)>> windows)
    {
        var matched = new HashSet<string>(StringComparer.Ordinal);
        if (windows.Count == 0)
        {
            return matched;
        }

        foreach (string source in sources)
        {
            foreach ((ulong hash, int start) in Windows(source))
            {
                if (!windows.TryGetValue(hash, out List<(int Change, string Window)>? candidates))
                {
                    continue;
                }

                foreach ((int _, string window) in candidates)
                {
                    if (string.CompareOrdinal(source, start, window, 0, MIN_COPIED_CHARS) == 0)
                    {
                        _ = matched.Add(window);
                    }
                }
            }
        }

        return matched;
    }

    private static IEnumerable<(ulong Hash, int Start)> Windows(string text)
    {
        if (text.Length < MIN_COPIED_CHARS)
        {
            yield break;
        }

        ulong hash = 0;
        for (int index = 0; index < MIN_COPIED_CHARS; index++)
        {
            hash = (hash * HASH_BASE) + text[index];
        }

        yield return (hash, 0);
        for (int start = 1; start + MIN_COPIED_CHARS <= text.Length; start++)
        {
            hash = ((hash - (text[start - 1] * _dropFactor)) * HASH_BASE) + text[start + MIN_COPIED_CHARS - 1];
            yield return (hash, start);
        }
    }

    private void AddNames(EditorAgentReportSummary report)
    {
        AddName(report.Counterparty);
        foreach (string name in report.DocumentNames)
        {
            AddDocumentName(name);
        }
    }

    private void AddName(string? name)
    {
        string normalized = Normalize(name ?? string.Empty);
        if (normalized.Length >= MIN_NAME_CHARS && normalized != NO_COUNTERPARTY)
        {
            _ = _names.Add(normalized);
        }
    }

    private void AddDocumentName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        AddName(name);
        AddName(Path.GetFileNameWithoutExtension(name));
    }

    private static bool ContainsWord(string text, string name)
    {
        int at = text.IndexOf(name, StringComparison.Ordinal);
        while (at >= 0)
        {
            bool startsWord = at == 0 || !char.IsLetterOrDigit(text[at - 1]);
            int end = at + name.Length;
            bool endsWord = end == text.Length || !char.IsLetterOrDigit(text[end]);
            if (startsWord && endsWord)
            {
                return true;
            }

            at = text.IndexOf(name, at + 1, StringComparison.Ordinal);
        }

        return false;
    }

    /// <summary>Every string in a change's JSON value: a field's text, or every string of an added node.</summary>
    private static IEnumerable<string> StringsOf(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        try
        {
            return JToken.Parse(json).SelectTokens("$..*").Append(JToken.Parse(json))
                .Where(static token => token.Type == JTokenType.String)
                .Select(static token => token.Value<string>()!)
                .ToList();
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return [json];
        }
    }

    public static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        bool space = false;
        foreach (char character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                space = builder.Length > 0;
                continue;
            }

            if (space)
            {
                _ = builder.Append(' ');
                space = false;
            }

            _ = builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    private static ulong Power(ulong value, int exponent)
    {
        ulong result = 1;
        for (int index = 0; index < exponent; index++)
        {
            result *= value;
        }

        return result;
    }
}

/// <summary>
/// The workflow's own text as the report-text check reads it, normalized once: only what its
/// author wrote, never what a test run left on it, since a test run's output can hold report text.
/// </summary>
public sealed class EditorAgentWorkflowText
{
    public EditorAgentWorkflowText(IEnumerable<string> strings)
    {
        Texts = [.. strings.Select(EditorAgentReportLeaks.Normalize)];
        Figures = new HashSet<string>(Texts.SelectMany(EditorAgentReportLeaks.Figures), StringComparer.Ordinal);
    }

    public IReadOnlyList<string> Texts { get; }

    public IReadOnlySet<string> Figures { get; }

    /// <summary>The authored strings of a workflow file, its run evidence left out.</summary>
    public static EditorAgentWorkflowText Authored(JObject workflow)
    {
        return new EditorAgentWorkflowText(WorkflowRunEvidence.AuthoredStrings(workflow));
    }
}

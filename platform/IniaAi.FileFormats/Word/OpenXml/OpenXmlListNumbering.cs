using DocumentFormat.OpenXml.Wordprocessing;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace IniaAi.FileFormats.Word.OpenXml;

/// <summary>
/// Counts list items in document order and writes the label Word shows: "3.", "(b)", "Article 2.1". Lists that share a
/// definition share their counters, a start override restarts them, and an item resets the levels below it.
/// </summary>
internal sealed partial class OpenXmlListNumbering
{
    private const int LEVELS = 9;

    private readonly Numbering? _numbering;
    private readonly Dictionary<int, int?[]> _countersByDefinition = [];
    private readonly HashSet<int> _seenInstances = [];

    public OpenXmlListNumbering(Numbering? numbering)
    {
        _numbering = numbering;
    }

    /// <summary>The kind of list and the label of the next item; the label is empty for a bullet.</summary>
    public (ListKind Kind, string Label) Next(int numId, int level)
    {
        level = Math.Clamp(level, 0, LEVELS - 1);
        NumberingInstance? instance = _numbering?.Elements<NumberingInstance>().FirstOrDefault(candidate => candidate.NumberID?.Value == numId);
        int? definitionId = instance?.AbstractNumId?.Val?.Value;
        AbstractNum? definition = _numbering?.Elements<AbstractNum>().FirstOrDefault(candidate => candidate.AbstractNumberId?.Value == definitionId);
        if (instance is null || definition is null || definitionId is not int key)
        {
            return (ListKind.Numbered, string.Empty);
        }

        var levels = new Level?[LEVELS];
        for (int l = 0; l < LEVELS; l++)
        {
            levels[l] = LevelDefinition(instance, definition, l);
        }

        if (levels[level]?.NumberingFormat?.Val?.Value == NumberFormatValues.Bullet)
        {
            return (ListKind.Bullet, string.Empty);
        }

        if (!_countersByDefinition.TryGetValue(key, out int?[]? counters))
        {
            counters = new int?[LEVELS];
            _countersByDefinition[key] = counters;
        }

        if (_seenInstances.Add(numId))
        {
            foreach (LevelOverride levelOverride in instance.Elements<LevelOverride>())
            {
                int? overridden = levelOverride.LevelIndex?.Value;
                if (overridden is int index and >= 0 and < LEVELS && levelOverride.StartOverrideNumberingValue?.Val?.Value is int start)
                {
                    counters[index] = start - 1;
                }
            }
        }

        counters[level] = counters[level] is int current ? current + 1 : Start(levels[level]);
        for (int deeper = level + 1; deeper < LEVELS; deeper++)
        {
            counters[deeper] = null;
        }

        bool legal = levels[level]?.IsLegalNumberingStyle is not null;
        string label = LevelText(levels[level]);
        label = PlaceholderPattern().Replace(label, match =>
        {
            int referenced = match.Groups[1].Value[0] - '1';
            int value = counters[referenced] ?? Start(levels[referenced]);
            NumberFormatValues? format = legal && referenced < level ? NumberFormatValues.Decimal : levels[referenced]?.NumberingFormat?.Val?.Value;
            return ListLabels.Format(value, format);
        });
        return (ListKind.Numbered, label.Trim());
    }

    private static Level? LevelDefinition(NumberingInstance instance, AbstractNum definition, int level)
    {
        Level? overridden = instance.Elements<LevelOverride>()
            .FirstOrDefault(candidate => candidate.LevelIndex?.Value == level)?.GetFirstChild<Level>();
        return overridden ?? definition.Elements<Level>().FirstOrDefault(candidate => candidate.LevelIndex?.Value == level);
    }

    private static int Start(Level? level)
    {
        return level?.StartNumberingValue?.Val?.Value ?? 1;
    }

    private static string LevelText(Level? level)
    {
        return level?.LevelText?.Val?.Value ?? string.Empty;
    }

    [GeneratedRegex("%([1-9])")]
    private static partial Regex PlaceholderPattern();
}

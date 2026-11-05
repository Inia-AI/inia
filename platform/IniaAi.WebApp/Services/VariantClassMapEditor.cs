using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.WebApp.Services;

/// <summary>
/// Pure logic for the parameterized-section variant class-map editor: deriving the mapping
/// rows from the classes a section's blocks actually use, and shaping the rows back into
/// the persisted <see cref="SectionVariant.ClassMap"/> dictionary.
/// </summary>
public static class VariantClassMapEditor
{
    public class Row
    {
        public string Source { get; set; } = string.Empty;

        /// <summary>Target classes for this source; empty means "unchanged" (pass-through).</summary>
        public List<string> Targets { get; set; } = [];

        /// <summary>True when the row comes from a saved mapping whose source class is no longer used by any block.</summary>
        public bool IsStale { get; set; }
    }

    /// <summary>
    /// Distinct, sorted document classes declared on the section's blocks, excluding the
    /// "Any"/"None" sentinels (they always pass through the engine's class map unchanged).
    /// </summary>
    public static List<string> CollectBlockClasses(Section section)
    {
        return [.. section.Blocks
            .SelectMany(b => b.SupportedDocumentClasses)
            .Where(c => !VariantDocumentClassEditor.IsSentinel(c))
            .Distinct()
            .Order()];
    }

    /// <summary>
    /// One row per block class (targets from the saved map, else unchanged), then one stale
    /// row per saved mapping whose source no longer appears on any block.
    /// </summary>
    public static List<Row> DeriveRows(IReadOnlyList<string> blockClasses, Dictionary<string, List<string>>? savedMap)
    {
        List<Row> rows = [.. blockClasses.Select(source => new Row
        {
            Source = source,
            Targets = savedMap?.GetValueOrDefault(source)?.ToList() ?? [],
        })];

        if (savedMap is not null)
        {
            rows.AddRange(savedMap
                .Where(entry => !blockClasses.Contains(entry.Key))
                .OrderBy(entry => entry.Key)
                .Select(entry => new Row
                {
                    Source = entry.Key,
                    Targets = [.. entry.Value],
                    IsStale = true,
                }));
        }

        return rows;
    }

    /// <summary>
    /// Applies a multi-select change to a row's targets, enforcing Any/None mutual exclusion:
    /// selecting a sentinel replaces the whole selection with it, selecting a real class
    /// strips any sentinel.
    /// </summary>
    public static List<string> NormalizeTargets(IReadOnlyCollection<string> previous, IReadOnlyCollection<string> selected)
    {
        List<string> added = [.. selected.Except(previous)];

        string? addedSentinel = added.FirstOrDefault(VariantDocumentClassEditor.IsSentinel);
        if (addedSentinel is not null)
        {
            return [addedSentinel];
        }

        return added.Count > 0
            ? [.. selected.Where(c => !VariantDocumentClassEditor.IsSentinel(c))]
            : [.. selected];
    }

    /// <summary>
    /// Shapes the rows into the persisted map. Unchanged (empty) and identity rows are
    /// omitted; the engine passes unmapped classes through.
    /// </summary>
    public static Dictionary<string, List<string>> BuildMap(IEnumerable<Row> rows)
    {
        return rows
            .Where(r => r.Targets.Count > 0 && !(r.Targets.Count == 1 && r.Targets[0] == r.Source))
            .ToDictionary(r => r.Source, r => r.Targets.ToList());
    }
}

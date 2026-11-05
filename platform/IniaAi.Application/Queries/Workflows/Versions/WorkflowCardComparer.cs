using IniaAi.WebApi.Contracts.Enums;
using IniaAi.WebApi.Contracts.Responses.WorkflowVersions;

namespace IniaAi.Application.Queries.Workflows.Versions;

/// <summary>
/// Pure comparison of currency cards from several deployments into a grid that shows, per workflow,
/// how each deployment's saved version relates to the benchmark — the most recently saved version
/// across all compared deployments. Comparing version-hash lineages distinguishes a deployment that
/// is simply behind from one that has diverged (edited on a separate line) or is ahead. No data
/// access — operates only on the cards given.
/// </summary>
public static class WorkflowCardComparer
{
    public static CurrencyComparisonDto Compare(IReadOnlyList<CurrencyCardDto> cards)
    {
        List<string> instanceNames = [.. cards
            .Select(card => card.InstanceName)
            .Distinct()
            .OrderBy(name => name, StringComparer.Ordinal)];

        List<CurrencyComparisonRowDto> rows = [.. cards
            .SelectMany(card => card.Workflows.Select(entry => entry.WorkflowTypeId))
            .Distinct()
            .Select(workflowTypeId => BuildRow(workflowTypeId, instanceNames, cards))
            .OrderBy(row => row.WorkflowTypeName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.WorkflowTypeId, StringComparer.Ordinal)];

        return new CurrencyComparisonDto(instanceNames, rows);
    }

    private static CurrencyComparisonRowDto BuildRow(
        string workflowTypeId,
        List<string> instanceNames,
        IReadOnlyList<CurrencyCardDto> cards)
    {
        var entryByInstance = new Dictionary<string, CurrencyCardEntryDto>(StringComparer.Ordinal);
        foreach (CurrencyCardDto card in cards)
        {
            CurrencyCardEntryDto? entry = card.Workflows.FirstOrDefault(e => e.WorkflowTypeId == workflowTypeId);
            if (entry is not null)
            {
                entryByInstance[card.InstanceName] = entry;
            }
        }

        List<CurrencyCardEntryDto> newestFirst = [.. entryByInstance.Values
            .OrderByDescending(entry => entry.SavedUtc)
            .ThenByDescending(entry => entry.ContentHash, StringComparer.Ordinal)];
        CurrencyCardEntryDto? benchmark = newestFirst.FirstOrDefault();
        string workflowTypeName = newestFirst
            .Select(entry => entry.WorkflowTypeName)
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
            ?? workflowTypeId;

        var cells = new List<CurrencyComparisonCellDto>();
        foreach (string instanceName in instanceNames)
        {
            if (!entryByInstance.TryGetValue(instanceName, out CurrencyCardEntryDto? entry))
            {
                cells.Add(new CurrencyComparisonCellDto(instanceName, null, CurrencyCellStatus.Missing));
                continue;
            }

            CurrencyCellStatus status = Classify(entry, benchmark!);
            cells.Add(new CurrencyComparisonCellDto(instanceName, entry.ShortCode, status));
        }

        return new CurrencyComparisonRowDto(workflowTypeId, workflowTypeName, cells);
    }

    private static CurrencyCellStatus Classify(CurrencyCardEntryDto entry, CurrencyCardEntryDto benchmark)
    {
        if (string.Equals(entry.ContentHash, benchmark.ContentHash, StringComparison.Ordinal))
        {
            return CurrencyCellStatus.Match;
        }

        if (Contains(benchmark.HistoryHashes, entry.ContentHash))
        {
            return CurrencyCellStatus.Behind;
        }

        if (Contains(entry.HistoryHashes, benchmark.ContentHash))
        {
            return CurrencyCellStatus.Ahead;
        }

        return CurrencyCellStatus.Diverged;
    }

    private static bool Contains(IReadOnlyList<string>? historyHashes, string contentHash)
    {
        return historyHashes is not null
            && historyHashes.Any(hash => string.Equals(hash, contentHash, StringComparison.Ordinal));
    }
}

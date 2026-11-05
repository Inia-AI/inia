using ErrorOr;
using IniaAi.Application.Errors;
using IniaAi.WebApi.Contracts.Dto;

namespace IniaAi.Application.Commands.Counterparties.ImportCounterparties;

/// <summary>
/// Parents-first order over the rows that may land, so a child always finds its parent's
/// landing before its own turn. A parent outside the bundle is not ordered; the importer
/// resolves it on the target. A cycle refuses the whole bundle.
/// </summary>
internal static class CounterpartyBundleOrder
{
    private enum Visit
    {
        InProgress,
        Done,
    }

    public static ErrorOr<List<CounterpartyBundleEntryDto>> ParentsFirst(IReadOnlyList<CounterpartyBundleEntryDto> entries)
    {
        Dictionary<string, CounterpartyBundleEntryDto> byId = entries.ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);
        var state = new Dictionary<string, Visit>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<CounterpartyBundleEntryDto>(entries.Count);
        var chain = new List<CounterpartyBundleEntryDto>();

        foreach (CounterpartyBundleEntryDto entry in entries)
        {
            chain.Clear();
            CounterpartyBundleEntryDto? current = entry;
            while (current is not null)
            {
                if (state.TryGetValue(current.Id, out Visit visit))
                {
                    if (visit is Visit.Done)
                    {
                        break;
                    }

                    string cycleStart = current.Id;
                    int start = chain.FindIndex(e => string.Equals(e.Id, cycleStart, StringComparison.OrdinalIgnoreCase));
                    return CounterpartyBundleErrorFactory.Cycle(chain[start..].Select(e => e.Id));
                }

                state[current.Id] = Visit.InProgress;
                chain.Add(current);
                current = !string.IsNullOrEmpty(current.ParentId) && byId.TryGetValue(current.ParentId, out CounterpartyBundleEntryDto? parent)
                    ? parent
                    : null;
            }

            for (int index = chain.Count - 1; index >= 0; index--)
            {
                state[chain[index].Id] = Visit.Done;
                ordered.Add(chain[index]);
            }
        }

        return ordered;
    }
}

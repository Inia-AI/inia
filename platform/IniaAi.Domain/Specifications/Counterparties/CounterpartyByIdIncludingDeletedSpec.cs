using Ardalis.Specification;
using IniaAi.Domain.Entities.Counterparties;

namespace IniaAi.Domain.Specifications.Counterparties;

/// <summary>
/// Specification for retrieving a counterparty by ID regardless of its soft-delete state,
/// with its CounterpartyType relationships eagerly loaded. Bypasses the global soft-delete
/// query filter so callers can distinguish an ID held by a live record from one held by a
/// previously deleted record. The result is tracked so callers may mutate and persist it.
/// </summary>
public sealed class CounterpartyByIdIncludingDeletedSpec : Specification<Counterparty>
{
    public CounterpartyByIdIncludingDeletedSpec(string id)
    {
        _ = Query
            .Where(c => c.Id == id)
            .Include(c => c.CounterpartyTypes)
            .IgnoreQueryFilters();
    }
}

using Ardalis.Specification;
using IniaAi.Domain.Entities.Counterparties;

namespace IniaAi.Domain.Specifications.Counterparties;

/// <summary>
/// Specification for filtering counterparties by organization ID.
/// Can be combined with other specifications for more complex queries.
/// </summary>
public sealed class CounterpartiesByOrganizationSpec : Specification<Counterparty>
{
    public CounterpartiesByOrganizationSpec(string organizationId)
    {
        _ = Query.Where(c => c.OrganizationId == organizationId);
    }
}
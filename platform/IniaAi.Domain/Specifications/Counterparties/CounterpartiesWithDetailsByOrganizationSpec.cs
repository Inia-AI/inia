using Ardalis.Specification;
using IniaAi.Domain.Entities.Counterparties;

namespace IniaAi.Domain.Specifications.Counterparties;

/// <summary>
/// Specification for retrieving counterparties by organization ID with their related details eagerly loaded.
/// Includes Children, Parent, and CounterpartyType relationships.
/// Optionally filters by allowed counterparty type IDs.
/// </summary>
public sealed class CounterpartiesWithDetailsByOrganizationSpec : Specification<Counterparty>
{
    public CounterpartiesWithDetailsByOrganizationSpec(string organizationId, IReadOnlyCollection<string>? counterpartyTypeIds = null)
    {
        _ = Query
            .Where(c => c.OrganizationId == organizationId)
            .Include(c => c.Children)
            .Include(c => c.Parent)
            .Include(c => c.CounterpartyTypes)
            .AsSplitQuery();

        if (counterpartyTypeIds is { Count: > 0 })
        {
            _ = Query.Where(c => c.CounterpartyTypes.Any(t => counterpartyTypeIds.Contains(t.Id)));
        }
    }
}

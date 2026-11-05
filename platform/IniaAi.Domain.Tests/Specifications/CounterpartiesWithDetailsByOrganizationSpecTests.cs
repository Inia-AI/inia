using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Specifications.Counterparties;

namespace IniaAi.Domain.Tests.Specifications;

/// <summary>
/// The spec eagerly loads several collection navigation properties; in one SQL statement those
/// multiply into a cartesian row explosion (EF's MultipleCollectionInclude warning), so it must
/// stay a split query.
/// </summary>
public class CounterpartiesWithDetailsByOrganizationSpecTests
{
    [Fact]
    public void FiltersByOrganizationInTheQuery()
    {
        var spec = new CounterpartiesWithDetailsByOrganizationSpec("org-a");

        Func<Counterparty, bool> filter = spec.WhereExpressions.Single().FilterFunc;

        Assert.True(filter(new Counterparty { OrganizationId = "org-a" }));
        Assert.False(filter(new Counterparty { OrganizationId = "org-b" }));
    }

    [Fact]
    public void SplitsCollectionIncludesAcrossQueries()
    {
        Assert.True(new CounterpartiesWithDetailsByOrganizationSpec("org-a").AsSplitQuery);
    }
}

using IniaAi.Domain.Entities.Counterparties;
using System.Linq.Expressions;

namespace IniaAi.Domain.Specifications.Common;

/// <summary>
/// Reusable expression library for Counterparty entity filtering.
/// Provides common counterparty query predicates that can be reused across specifications.
/// </summary>
public static class CounterpartyExpressions
{
    /// <summary>
    /// Expression for filtering by organization ID.
    /// </summary>
    public static Expression<Func<Counterparty, bool>> ByOrganization(string organizationId)
    {
        return c => c.OrganizationId == organizationId;
    }

    /// <summary>
    /// Expression for filtering by counterparty ID.
    /// </summary>
    public static Expression<Func<Counterparty, bool>> ById(string id)
    {
        return c => c.Id == id;
    }

    /// <summary>
    /// Expression for filtering by counterparty name (contains search).
    /// </summary>
    public static Expression<Func<Counterparty, bool>> ByNameContains(string searchTerm)
    {
        return c => c.Name.ToLower().Contains(searchTerm.ToLower());
    }

    /// <summary>
    /// Expression for filtering by counterparty type ID.
    /// </summary>
    public static Expression<Func<Counterparty, bool>> ByCounterpartyType(string counterpartyTypeId)
    {
        return c => c.CounterpartyTypes.Any(t => t.Id == counterpartyTypeId);
    }

    /// <summary>
    /// Expression for filtering parent counterparties (no parent).
    /// </summary>
    public static Expression<Func<Counterparty, bool>> IsParent =>
        c => c.ParentId == null;

    /// <summary>
    /// Expression for filtering child counterparties of a specific parent.
    /// </summary>
    public static Expression<Func<Counterparty, bool>> ByParent(string parentId)
    {
        return c => c.ParentId == parentId;
    }
}

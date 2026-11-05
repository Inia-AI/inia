using Ardalis.Specification;

namespace IniaAi.Domain.Specifications.Base;

/// <summary>
/// Combines two specifications with AND logic.
/// Allows composition of multiple filtering criteria.
/// </summary>
/// <typeparam name="T">The entity type</typeparam>
public class AndSpecification<T> : Specification<T> where T : class
{
    public AndSpecification(ISpecification<T> left, ISpecification<T> right)
    {
        _ = Query.Where(left.WhereExpressions.First().Filter)
             .Where(right.WhereExpressions.First().Filter);

        // Note: Includes and tracking behavior cannot be easily combined from ISpecification
        // This is a limitation of the composition approach with Ardalis.Specification
        // Consider using inheritance from base specs instead for include/tracking requirements
    }
}
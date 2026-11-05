using Ardalis.Specification;
using System.Linq.Expressions;

namespace IniaAi.Domain.Specifications.Base;

/// <summary>
/// Negates a specification with NOT logic.
/// Useful for creating exclusion criteria.
/// </summary>
/// <typeparam name="T">The entity type</typeparam>
public class NotSpecification<T> : Specification<T> where T : class
{
    public NotSpecification(ISpecification<T> specification)
    {
        Expression<Func<T, bool>> originalExpression = specification.WhereExpressions.First().Filter;

        // Create NOT expression
        ParameterExpression parameter = Expression.Parameter(typeof(T), "x");
        Expression body = ExpressionReplacer.Replace(originalExpression.Body, originalExpression.Parameters[0], parameter);
        UnaryExpression notExpression = Expression.Not(body);
        var lambda = Expression.Lambda<Func<T, bool>>(notExpression, parameter);

        _ = Query.Where(lambda);

        // Note: Includes and tracking behavior cannot be easily copied from ISpecification
        // This is a limitation of the composition approach with Ardalis.Specification
        // Consider using inheritance from base specs instead for include/tracking requirements
    }
}
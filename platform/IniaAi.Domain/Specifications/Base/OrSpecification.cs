using Ardalis.Specification;
using System.Linq.Expressions;

namespace IniaAi.Domain.Specifications.Base;

/// <summary>
/// Combines two specifications with OR logic.
/// Useful for creating alternative filtering criteria.
/// </summary>
/// <typeparam name="T">The entity type</typeparam>
public class OrSpecification<T> : Specification<T> where T : class
{
    public OrSpecification(ISpecification<T> left, ISpecification<T> right)
    {
        Expression<Func<T, bool>> leftExpression = left.WhereExpressions.First().Filter;
        Expression<Func<T, bool>> rightExpression = right.WhereExpressions.First().Filter;

        // Combine expressions with OR logic
        ParameterExpression parameter = Expression.Parameter(typeof(T), "x");
        Expression leftBody = ExpressionReplacer.Replace(leftExpression.Body, leftExpression.Parameters[0], parameter);
        Expression rightBody = ExpressionReplacer.Replace(rightExpression.Body, rightExpression.Parameters[0], parameter);

        BinaryExpression orExpression = Expression.OrElse(leftBody, rightBody);
        var lambda = Expression.Lambda<Func<T, bool>>(orExpression, parameter);

        _ = Query.Where(lambda);

        // Note: Includes and tracking behavior cannot be easily combined from ISpecification
        // This is a limitation of the composition approach with Ardalis.Specification
        // Consider using inheritance from base specs instead for include/tracking requirements
    }
}

/// <summary>
/// Helper class for replacing parameters in expressions.
/// </summary>
internal class ExpressionReplacer : ExpressionVisitor
{
    private readonly Expression _oldValue;
    private readonly Expression _newValue;

    private ExpressionReplacer(Expression oldValue, Expression newValue)
    {
        _oldValue = oldValue;
        _newValue = newValue;
    }

    public static Expression Replace(Expression expression, Expression oldValue, Expression newValue)
    {
        return new ExpressionReplacer(oldValue, newValue).Visit(expression)
            ?? throw new InvalidOperationException("Expression replacement resulted in null");
    }

    public override Expression? Visit(Expression? node)
    {
        return node == _oldValue ? _newValue : base.Visit(node);
    }
}

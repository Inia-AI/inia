using Ardalis.Specification;

namespace IniaAi.Domain.Specifications.Base;

/// <summary>
/// Extension methods for combining specifications using fluent syntax.
/// Enables composition like: spec1.And(spec2).Or(spec3)
/// </summary>
public static class SpecificationExtensions
{
    /// <summary>
    /// Combines two specifications with AND logic.
    /// </summary>
    /// <typeparam name="T">The entity type</typeparam>
    /// <param name="left">The first specification</param>
    /// <param name="right">The second specification</param>
    /// <returns>A new specification that combines both with AND logic</returns>
    public static AndSpecification<T> And<T>(this ISpecification<T> left, ISpecification<T> right)
        where T : class
    {
        return new AndSpecification<T>(left, right);
    }

    /// <summary>
    /// Combines two specifications with OR logic.
    /// </summary>
    /// <typeparam name="T">The entity type</typeparam>
    /// <param name="left">The first specification</param>
    /// <param name="right">The second specification</param>
    /// <returns>A new specification that combines both with OR logic</returns>
    public static OrSpecification<T> Or<T>(this ISpecification<T> left, ISpecification<T> right)
        where T : class
    {
        return new OrSpecification<T>(left, right);
    }

    /// <summary>
    /// Creates a negated specification (NOT logic).
    /// </summary>
    /// <typeparam name="T">The entity type</typeparam>
    /// <param name="specification">The specification to negate</param>
    /// <returns>A new specification that negates the original</returns>
    public static NotSpecification<T> Not<T>(this ISpecification<T> specification)
        where T : class
    {
        return new NotSpecification<T>(specification);
    }
}
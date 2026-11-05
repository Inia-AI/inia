using Ardalis.Specification;
using System.Linq.Expressions;

namespace IniaAi.Domain.Specifications.Base;

/// <summary>
/// Fluent builder for creating complex specifications.
/// Provides a chainable API for building specifications step by step.
/// </summary>
/// <typeparam name="T">The entity type</typeparam>
public class SpecificationBuilder<T> where T : class
{
    private ISpecification<T>? _specification;

    /// <summary>
    /// Starts building with a base specification.
    /// </summary>
    /// <param name="specification">The initial specification</param>
    /// <returns>The builder for chaining</returns>
    public static SpecificationBuilder<T> Create(ISpecification<T> specification)
    {
        return new SpecificationBuilder<T> { _specification = specification };
    }

    /// <summary>
    /// Starts building with a lambda expression.
    /// </summary>
    /// <param name="predicate">The filtering predicate</param>
    /// <returns>The builder for chaining</returns>
    public static SpecificationBuilder<T> Create(Expression<Func<T, bool>> predicate)
    {
        return Create(new DirectSpecification<T>(predicate));
    }

    /// <summary>
    /// Adds an AND condition to the specification.
    /// </summary>
    /// <param name="specification">The specification to combine with AND</param>
    /// <returns>The builder for chaining</returns>
    public SpecificationBuilder<T> And(ISpecification<T> specification)
    {
        _specification = _specification?.And(specification) ?? specification;
        return this;
    }

    /// <summary>
    /// Adds an AND condition using a lambda expression.
    /// </summary>
    /// <param name="predicate">The filtering predicate</param>
    /// <returns>The builder for chaining</returns>
    public SpecificationBuilder<T> And(Expression<Func<T, bool>> predicate)
    {
        return And(new DirectSpecification<T>(predicate));
    }

    /// <summary>
    /// Adds an OR condition to the specification.
    /// </summary>
    /// <param name="specification">The specification to combine with OR</param>
    /// <returns>The builder for chaining</returns>
    public SpecificationBuilder<T> Or(ISpecification<T> specification)
    {
        _specification = _specification?.Or(specification) ?? specification;
        return this;
    }

    /// <summary>
    /// Adds an OR condition using a lambda expression.
    /// </summary>
    /// <param name="predicate">The filtering predicate</param>
    /// <returns>The builder for chaining</returns>
    public SpecificationBuilder<T> Or(Expression<Func<T, bool>> predicate)
    {
        return Or(new DirectSpecification<T>(predicate));
    }

    /// <summary>
    /// Builds the final specification.
    /// </summary>
    /// <returns>The composed specification</returns>
    public ISpecification<T> Build()
    {
        return _specification ?? throw new InvalidOperationException("No specification has been added to the builder.");
    }
}

/// <summary>
/// Simple specification that wraps a lambda expression.
/// Used internally by the builder for direct predicates.
/// </summary>
/// <typeparam name="T">The entity type</typeparam>
internal class DirectSpecification<T> : Specification<T> where T : class
{
    public DirectSpecification(Expression<Func<T, bool>> predicate)
    {
        _ = Query.Where(predicate);
    }
}
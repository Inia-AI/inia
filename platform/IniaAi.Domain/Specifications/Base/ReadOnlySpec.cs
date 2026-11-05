using Ardalis.Specification;

namespace IniaAi.Domain.Specifications.Base;

/// <summary>
/// Base specification for read-only queries with AsNoTracking optimization.
/// Use this for queries where you don't intend to modify the entities.
/// </summary>
/// <typeparam name="T">The entity type</typeparam>
public class ReadOnlySpec<T> : Specification<T> where T : class
{
    public ReadOnlySpec()
    {
        _ = Query.AsNoTracking();
    }
}
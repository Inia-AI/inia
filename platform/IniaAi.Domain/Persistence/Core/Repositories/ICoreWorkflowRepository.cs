using Ardalis.Specification;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Domain.Core.Interfaces;

public interface ICoreWorkflowRepository
{
    Task<Workflow?> GetByIdAsync(string id);
    Task<Workflow?> GetByTypeIdAsync(string? workflowTypeId);
    Task<Workflow?> GetByTypeIdAsNoTrackingAsync(string? workflowTypeId);
    Task AddAsync(Workflow workflow);
    void Remove(Workflow workflow);

    /// <summary>
    /// Gets all entities matching a specification. This method is not recommended for large datasets.
    /// </summary>
    /// <param name="specification">The specification to match</param>
    /// <returns>An <see cref="IQueryable{T}"/> that contains elements that satisfy the specification</returns>
    IQueryable<Workflow> GetAll(ISpecification<Workflow> specification);
}

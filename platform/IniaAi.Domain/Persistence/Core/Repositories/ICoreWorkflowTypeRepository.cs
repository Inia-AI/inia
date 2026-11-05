using IniaAi.Domain.Entities.Workflows;
using System.Linq.Expressions;

namespace IniaAi.Domain.Core.Interfaces;

public interface ICoreWorkflowTypeRepository
{
    Task<WorkflowType?> GetByIdAsync(string id);
    Task<IEnumerable<WorkflowType>> GetAllAsync();

    Task<IEnumerable<WorkflowType>> GetAllWithDocumentClassesAsync();
    Task<IEnumerable<WorkflowType>> FindAsync(Expression<Func<WorkflowType, bool>> predicate);
    Task AddAsync(WorkflowType workflowType);
    void Remove(WorkflowType workflowType);
    Task<bool> ExistsAsync(string id);

    /// <summary>
    /// Gets all entities. This method is not recommended
    /// </summary>
    /// <returns>The <see cref="IQueryable{T}"/>.</returns>
    IQueryable<WorkflowType> GetAll();
}

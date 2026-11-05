using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace IniaAi.Infrastructure.Persistence.Core.Repositories;

public class CoreWorkflowTypeRepository : ICoreWorkflowTypeRepository
{
    private readonly DbSet<WorkflowType> _workflowTypes;
    private readonly DbSet<Workflow> _workflows;

    public CoreWorkflowTypeRepository(CoreDbContext context)
    {
        _workflowTypes = context.Set<WorkflowType>();
        _workflows = context.Set<Workflow>();
    }

    private IQueryable<WorkflowType> NotRetired => _workflowTypes.Where(WorkflowType.NotRetired);

    public async Task<WorkflowType?> GetByIdAsync(string id)
    {
        return await NotRetired
            .Include(wt => wt.Workflows)
            .ThenInclude(w => w.Sections)
            .Include(wt => wt.CounterpartyTypeMappings)
            .FirstOrDefaultAsync(wt => wt.Id == id);
    }

    public async Task<IEnumerable<WorkflowType>> GetAllAsync()
    {
        return await NotRetired
            .Include(wt => wt.Workflows)
            .ThenInclude(w => w.Sections)
            .Include(wt => wt.CounterpartyTypeMappings)
            .ToListAsync();
    }

    public async Task<IEnumerable<WorkflowType>> GetAllWithDocumentClassesAsync()
    {
        return await NotRetired
            .Include(wt => wt.Workflows)
            .ThenInclude(w => w.DocumentClasses)
            .ToListAsync();
    }

    public async Task<IEnumerable<WorkflowType>> FindAsync(Expression<Func<WorkflowType, bool>> predicate)
    {
        return await NotRetired
            .Where(predicate)
            .ToListAsync();
    }

    public async Task AddAsync(WorkflowType workflowType)
    {
        _ = await _workflowTypes.AddAsync(workflowType);
    }

    public void Remove(WorkflowType workflowType)
    {
        foreach (Workflow workflow in workflowType.Workflows)
        {
            _ = _workflows.Remove(workflow);
        }

        _ = _workflowTypes.Remove(workflowType);
    }

    /// <summary>
    /// Counts a retired type too: its id stays taken until the weekly cleanup removes the row.
    /// </summary>
    public async Task<bool> ExistsAsync(string id)
    {
        return await _workflowTypes.AnyAsync(wt => wt.Id == id);
    }

    /// <summary>
    /// Gets all entities. This method is not recommended
    /// </summary>
    /// <returns>The <see cref="IQueryable{T}"/>.</returns>
    public IQueryable<WorkflowType> GetAll()
    {
        return NotRetired.AsNoTracking();
    }
}

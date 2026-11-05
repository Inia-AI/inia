using Ardalis.Specification;
using Ardalis.Specification.EntityFrameworkCore;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Infrastructure.Persistence.Core.Repositories;

public class CoreWorkflowRepository : ICoreWorkflowRepository
{
    private readonly DbSet<Workflow> _workflows;

    public CoreWorkflowRepository(CoreDbContext context)
    {
        _workflows = context.Set<Workflow>();
    }

    public async Task<Workflow?> GetByIdAsync(string id)
    {
        return await _workflows
            .AsSplitQuery()
            .Include(w => w.WorkflowType)
            .Include(w => w.Variables)
            .Include(w => w.DocumentClasses)
            .Include(w => w.RegressionFields)
            .Include(w => w.SignatureFields)
            .Include(w => w.Sections)
            .Include($"{nameof(Workflow.Sections)}.{nameof(Section.Blocks)}.{nameof(Block.TextInputs)}")
            .Include($"{nameof(Workflow.Sections)}.{nameof(Section.Blocks)}.{nameof(Block.RagSettings)}")
            .FirstOrDefaultAsync(qs => qs.Id == id);
    }

    public async Task<Workflow?> GetByTypeIdAsNoTrackingAsync(string? workflowTypeId)
    {
        return await WithGraph(_workflows.AsNoTracking())
            .Where(w => w.WorkflowTypeId == workflowTypeId)
            .OrderByDescending(w => w.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<Workflow?> GetByTypeIdAsync(string? workflowTypeId)
    {
        return await WithGraph(_workflows)
            .Where(w => w.WorkflowTypeId == workflowTypeId)
            .OrderByDescending(w => w.CreatedAt)
            .FirstOrDefaultAsync();
    }

    private static IQueryable<Workflow> WithGraph(IQueryable<Workflow> query)
    {
        return query
            .AsSplitQuery()
            .Include(w => w.WorkflowType)
            .Include(w => w.Variables)
            .Include(w => w.DocumentClasses)
            .Include(w => w.RegressionFields)
            .Include(w => w.SignatureFields)
            .Include(w => w.Sections)
            .Include($"{nameof(Workflow.Sections)}.{nameof(Section.Blocks)}.{nameof(Block.TextInputs)}")
            .Include($"{nameof(Workflow.Sections)}.{nameof(Section.Blocks)}.{nameof(Block.RagSettings)}");
    }

    public async Task AddAsync(Workflow workflow)
    {
        _ = await _workflows.AddAsync(workflow);
    }

    public void Remove(Workflow workflow)
    {
        _ = _workflows.Remove(workflow);
    }

    /// <summary>
    /// Gets all entities matching a specification. This method is not recommended for large datasets.
    /// </summary>
    /// <param name="specification">The specification to match</param>
    /// <returns>An <see cref="IQueryable{T}"/> that contains elements that satisfy the specification</returns>
    public IQueryable<Workflow> GetAll(ISpecification<Workflow> specification)
    {
        return _workflows.WithSpecification(specification);
    }
}

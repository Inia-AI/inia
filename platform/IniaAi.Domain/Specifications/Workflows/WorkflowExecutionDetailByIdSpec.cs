using Ardalis.Specification;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Domain.Specifications.Workflows;

/// <summary>
/// Specification for retrieving a workflow with all details needed for execution detail view.
/// Includes sections, blocks, block responses, text inputs, RAG settings, agent run trace
/// files and citation traces.
/// </summary>
public sealed class WorkflowExecutionDetailByIdSpec : Specification<Workflow>, ISingleResultSpecification<Workflow>
{
    public WorkflowExecutionDetailByIdSpec()
    {
        _ = Query
           .Include(w => w.Variables)
           .Include(w => w.DocumentClasses)
           .Include(w => w.RegressionFields)
           .Include(w => w.SignatureFields)
           .Include(w => w.Sections.OrderBy(s => s.Order))
               .ThenInclude(s => s.Blocks.OrderBy(b => b.Order))
                   .ThenInclude(b => b.BlockResponse)
           .Include(w => w.Sections.OrderBy(s => s.Order))
               .ThenInclude(s => s.Blocks.OrderBy(b => b.Order))
                   .ThenInclude(b => b.TextInputs)
           .Include(w => w.Sections.OrderBy(s => s.Order))
               .ThenInclude(s => s.Blocks.OrderBy(b => b.Order))
                   .ThenInclude(b => b.RagSettings)
           .Include(w => w.Sections.OrderBy(s => s.Order))
               .ThenInclude(s => s.Blocks.OrderBy(b => b.Order))
                   .ThenInclude(b => b.AgentRunTraceFiles)
           .Include(w => w.Sections.OrderBy(s => s.Order))
               .ThenInclude(s => s.Blocks.OrderBy(b => b.Order))
                   .ThenInclude(b => b.CitationTrace)
           .AsSplitQuery()
           .AsNoTracking();
    }
}

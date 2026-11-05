using Ardalis.Specification;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Domain.Specifications.Signatures;

/// <summary>
/// Loads the canonical non-deleted root workflow for a given WorkflowTypeId, together
/// with its ordered sections and every block in each section. Matches by
/// WorkflowTypeId only — the workflow definition (staff-authored signature defaults
/// included) is shared across every tenant granted access via
/// <c>WorkflowTypeOrganizationAccess</c>; per-tenant data lives in the override
/// tables, not on the Workflow row. Callers must enforce access via the junction
/// table before invoking this spec.
///
/// <para>
/// <c>OrderByDescending(CreatedAt)</c> mirrors <c>CoreWorkflowRepository.GetByTypeIdAsync</c>
/// so the signatures surface always resolves to the same Workflow row that the
/// execution engine runs against. Without this, legacy duplicate rows (from the
/// pre-shared per-tenant model) could cause signatures and execution to read
/// different rows with non-overlapping Block ids, silently mis-targeting overrides.
/// </para>
/// </summary>
public sealed class WorkflowWithSectionsAndBlocksByTypeSpec : Specification<Workflow>, ISingleResultSpecification<Workflow>
{
    public WorkflowWithSectionsAndBlocksByTypeSpec(string workflowTypeId)
    {
        _ = Query
            .Where(w => w.WorkflowTypeId == workflowTypeId)
            .OrderByDescending(w => w.CreatedAt)
            .Include(w => w.SignatureFields)
            .Include(w => w.Sections.OrderBy(s => s.Order))
                .ThenInclude(s => s.Blocks.OrderBy(b => b.Order))
            .AsSplitQuery()
            .AsNoTracking();
    }
}

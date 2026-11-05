using Ardalis.Specification;
using IniaAi.Domain.Entities.AgentRuntime;

namespace IniaAi.Domain.Specifications.AgentRuntime;

/// <summary>
/// Trace rows for one executed block, org-scoped inside the query by the row's own stamp —
/// resolving across tenants and filtering afterwards would leak cross-tenant existence.
/// </summary>
public sealed class AgentRunTraceFilesByBlockSpec : Specification<AgentRunTraceFile>
{
    public AgentRunTraceFilesByBlockSpec(string organizationId, string blockId)
    {
        _ = Query
            .Where(file => file.BlockId == blockId && file.OrganizationId == organizationId)
            .OrderBy(file => file.Path)
            .AsNoTracking();
    }
}

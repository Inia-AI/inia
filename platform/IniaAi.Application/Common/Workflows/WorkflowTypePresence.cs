using IniaAi.Domain.Entities.Workflows;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Common.Workflows;

public enum WorkflowTypePresence
{
    Missing,
    Live,
    Retired,
}

public static class WorkflowTypePresenceQuery
{
    /// <summary>
    /// One read of the type row: a retired type is neither missing, because its id stays taken until
    /// the weekly cleanup removes it, nor live, because nothing may change it.
    /// </summary>
    public static async Task<WorkflowTypePresence> ReadAsync(
        IQueryable<WorkflowType> workflowTypes,
        string workflowTypeId,
        CancellationToken cancellationToken)
    {
        var row = await workflowTypes
            .AsNoTracking()
            .Where(type => type.Id == workflowTypeId)
            .Select(type => new { type.RetiredAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return WorkflowTypePresence.Missing;
        }

        return row.RetiredAt is null ? WorkflowTypePresence.Live : WorkflowTypePresence.Retired;
    }

    /// <summary>The type's name, or its id when no row has it, for a message that names the type.</summary>
    public static async Task<string> ReadNameAsync(
        IQueryable<WorkflowType> workflowTypes,
        string workflowTypeId,
        CancellationToken cancellationToken)
    {
        return await workflowTypes
            .AsNoTracking()
            .Where(type => type.Id == workflowTypeId)
            .Select(type => type.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? workflowTypeId;
    }
}

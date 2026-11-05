using Ardalis.Specification;
using IniaAi.Domain.Entities.WorkflowExecutions;

namespace IniaAi.Domain.Specifications.WorkflowExecutions;

/// <summary>
/// The process starts, other than this one, last seen at or after the first time and before the
/// second: stopped, and recent enough that their runs can still be active.
/// </summary>
public sealed class LostHostBootsSpec : Specification<HostBoot>
{
    public LostHostBootsSpec(DateTime seenSince, DateTime lostBefore, string ownHostBootId)
    {
        _ = Query
            .Where(boot => boot.LastSeenAt >= seenSince && boot.LastSeenAt < lostBefore && boot.Id != ownHostBootId)
            .AsNoTracking();
    }
}

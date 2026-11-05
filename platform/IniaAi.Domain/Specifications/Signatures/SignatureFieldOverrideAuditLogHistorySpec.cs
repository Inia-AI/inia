using Ardalis.Specification;
using IniaAi.Domain.Entities.Signatures;

namespace IniaAi.Domain.Specifications.Signatures;

/// <summary>
/// Builds the History-tab query for <see cref="SignatureFieldOverrideAuditLog"/>: scoped
/// to a single <c>(OrganizationId, WorkflowTypeId)</c> with optional filters on
/// placeholder, actor, and date range. Newest-first ordering plus skip/take paging.
/// <c>AsNoTracking</c> — read-only path.
/// </summary>
public sealed class SignatureFieldOverrideAuditLogHistorySpec : Specification<SignatureFieldOverrideAuditLog>
{
    public SignatureFieldOverrideAuditLogHistorySpec(
        string organizationId,
        string workflowTypeId,
        string? placeholderName,
        string? actorUserId,
        DateTime? fromUtc,
        DateTime? toUtc,
        int skip,
        int take)
    {
        _ = Query
            .Where(x => x.OrganizationId == organizationId
                && x.WorkflowTypeId == workflowTypeId);

        if (!string.IsNullOrWhiteSpace(placeholderName))
        {
            _ = Query.Where(x => x.PlaceholderName == placeholderName);
        }

        if (!string.IsNullOrWhiteSpace(actorUserId))
        {
            _ = Query.Where(x => x.ActorUserId == actorUserId);
        }

        if (fromUtc.HasValue)
        {
            DateTime from = fromUtc.Value;
            _ = Query.Where(x => x.CreatedAt >= from);
        }

        if (toUtc.HasValue)
        {
            DateTime to = toUtc.Value;
            _ = Query.Where(x => x.CreatedAt <= to);
        }

        _ = Query
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Skip(skip)
            .Take(take)
            .AsNoTracking();
    }
}

/// <summary>
/// Companion count spec — same filter set as
/// <see cref="SignatureFieldOverrideAuditLogHistorySpec"/> but without ordering or
/// paging, used to populate <c>TotalCount</c> on the response DTO.
/// </summary>
public sealed class SignatureFieldOverrideAuditLogCountSpec : Specification<SignatureFieldOverrideAuditLog>
{
    public SignatureFieldOverrideAuditLogCountSpec(
        string organizationId,
        string workflowTypeId,
        string? placeholderName,
        string? actorUserId,
        DateTime? fromUtc,
        DateTime? toUtc)
    {
        _ = Query
            .Where(x => x.OrganizationId == organizationId
                && x.WorkflowTypeId == workflowTypeId);

        if (!string.IsNullOrWhiteSpace(placeholderName))
        {
            _ = Query.Where(x => x.PlaceholderName == placeholderName);
        }

        if (!string.IsNullOrWhiteSpace(actorUserId))
        {
            _ = Query.Where(x => x.ActorUserId == actorUserId);
        }

        if (fromUtc.HasValue)
        {
            DateTime from = fromUtc.Value;
            _ = Query.Where(x => x.CreatedAt >= from);
        }

        if (toUtc.HasValue)
        {
            DateTime to = toUtc.Value;
            _ = Query.Where(x => x.CreatedAt <= to);
        }
    }
}

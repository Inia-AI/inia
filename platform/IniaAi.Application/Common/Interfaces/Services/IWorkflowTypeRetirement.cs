using ErrorOr;

namespace IniaAi.Application.Common.Interfaces.Services;

/// <summary>
/// Result of purging the retired workflow types of one organization.
/// </summary>
public record RetiredWorkflowTypePurgeResult(
    IReadOnlyList<string> PurgedWorkflowTypeIds,
    IReadOnlyList<string> Errors);

/// <summary>
/// Deletes a workflow type in two steps: retiring it hides it from its organization at once and
/// soft-deletes what that organization ran with it, and the weekly cleanup purges the type itself
/// once the organization's retention window has passed.
/// </summary>
public interface IWorkflowTypeRetirement
{
    /// <summary>
    /// Retires a type that only <paramref name="organizationId"/> can reach: stamps it retired and
    /// soft-deletes, as <paramref name="userId"/>, its access, template, versions, signature overrides
    /// and validation schemas, and the organization's runs, executed workflows, reports, validation
    /// cases, and the documents of the type, with their stored files, that no run of another type
    /// uses. Refuses a type the platform owns, a type another organization can reach, and a type with
    /// a run in progress.
    /// </summary>
    Task<ErrorOr<Deleted>> RetireAsync(string organizationId, string userId, string workflowTypeId, CancellationToken ct = default);

    /// <summary>
    /// Hard-deletes every type <paramref name="organizationId"/> retired more than
    /// <paramref name="retentionDays"/> ago, with everything retiring soft-deleted in Core. Audit rows
    /// that name the type stay.
    /// </summary>
    Task<RetiredWorkflowTypePurgeResult> PurgeRetiredAsync(string organizationId, int retentionDays, CancellationToken ct = default);
}

namespace IniaAi.Application.Common.DataManagement;

/// <summary>
/// How many days an organization's soft-deleted records stay before the cleanup purges them, when
/// the organization has no cleanup settings of its own. The platform changes nothing, so the global
/// or default retention applies; a closed module may shorten it for its organizations.
/// </summary>
public interface IDeletedRecordRetention
{
    /// <summary>The retention in days for this organization, or null to keep the global or default retention.</summary>
    Task<int?> RetentionDaysAsync(string organizationId, CancellationToken cancellationToken);
}

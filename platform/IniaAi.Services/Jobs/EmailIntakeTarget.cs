namespace IniaAi.Services.Jobs;

/// <summary>
/// Who receives the documents an email expansion mints: a report job, or the editor's test-run
/// documents, which belong to no job.
/// </summary>
public sealed record EmailIntakeTarget(
    string OrganizationId,
    string? UserId,
    bool InputIsVouchedFor,
    string? JobId)
{
    public string RunLabel => JobId is null ? "an editor test run" : $"job {JobId}";
}

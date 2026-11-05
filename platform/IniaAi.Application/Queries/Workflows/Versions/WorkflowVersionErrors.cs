using ErrorOr;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Application.Queries.Workflows.Versions;

public static class WorkflowVersionErrors
{
    public const string NOT_FOUND_CODE = "WorkflowVersion.NotFound";
    public const string PAYLOAD_TRIMMED_CODE = "WorkflowVersion.PayloadTrimmed";
    public const string PAYLOAD_MISSING_CODE = "WorkflowVersion.PayloadMissing";
    public const string UNKNOWN_TYPE_CODE = "WorkflowTemplate.NotFound";

    /// <summary>
    /// One wording whether the version is missing or belongs to another workflow type, so a
    /// type-scoped caller learns nothing about other types by guessing version ids.
    /// </summary>
    public static Error NotFound(string versionId, string? workflowTypeId)
    {
        return Error.NotFound(
            NOT_FOUND_CODE,
            workflowTypeId is null
                ? $"Workflow version '{versionId}' was not found."
                : $"Workflow version '{versionId}' was not found on workflow template '{workflowTypeId}'.");
    }

    public static bool IsOutOfScope(WorkflowVersion version, string? workflowTypeId)
    {
        return workflowTypeId is not null
            && !string.Equals(version.WorkflowTypeId, workflowTypeId, StringComparison.Ordinal);
    }

    /// <summary>
    /// Null when the version's workflow JSON can be read. <see cref="WorkflowVersion.PayloadTrimmedAt"/>
    /// is the retention signal; a row with no payload and no trim stamp is damaged data and is
    /// refused too, rather than parsed as an empty document.
    /// </summary>
    public static Error? PayloadProblem(WorkflowVersion version)
    {
        if (version.PayloadTrimmedAt is not null)
        {
            return Error.NotFound(
                PAYLOAD_TRIMMED_CODE,
                $"The saved content of workflow version '{version.Id}' was cleared to reclaim storage, so it can no longer be opened or compared.");
        }

        if (version.WorkflowJsonCompressed is null || version.WorkflowJsonCompressed.Length == 0)
        {
            return Error.NotFound(
                PAYLOAD_MISSING_CODE,
                $"Workflow version '{version.Id}' has no stored content, so it cannot be opened or compared.");
        }

        return null;
    }

    public static Error UnknownType(string workflowTypeId)
    {
        return Error.NotFound(UNKNOWN_TYPE_CODE, $"Workflow template '{workflowTypeId}' was not found.");
    }
}

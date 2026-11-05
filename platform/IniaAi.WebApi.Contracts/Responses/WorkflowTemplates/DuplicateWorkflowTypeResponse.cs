namespace IniaAi.WebApi.Contracts.Responses.WorkflowTemplates;

/// <summary>
/// Result of duplicating a workflow template into a new workflow type.
/// </summary>
/// <param name="WorkflowTypeId">The identifier of the workflow type that was created.</param>
/// <param name="VersionId">The root version of the duplicate's own version chain.</param>
/// <param name="ContentHash">The canonical content hash of the duplicated template.</param>
/// <param name="SkippedCarriers">What deliberately did not carry, and why.</param>
public record DuplicateWorkflowTypeResponse(
    string WorkflowTypeId,
    string VersionId,
    string ContentHash,
    List<SkippedCarrierDto> SkippedCarriers);

/// <summary>
/// Something the duplicate deliberately did not inherit from its source.
/// </summary>
/// <param name="Carrier">What was not carried (e.g. <c>Organization access</c>).</param>
/// <param name="Reason">Why it was not carried, and what to do about it.</param>
public record SkippedCarrierDto(string Carrier, string Reason);

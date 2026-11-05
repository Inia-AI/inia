namespace IniaAi.WebApi.Contracts.Responses.WorkflowVersions;

/// <summary>
/// One keyset page of a workflow's version history, newest-first. <see cref="HasMore"/>
/// signals that older versions exist beyond this page (drive "Show older" from it), and
/// <see cref="TotalCount"/> is the full history size for the header count chip.
/// </summary>
public record WorkflowHistoryPageDto(
    IReadOnlyList<WorkflowVersionSummaryDto> Versions,
    bool HasMore,
    int TotalCount);

namespace IniaAi.WebApi.Contracts.Responses.ExecutedWorkflows;

/// <summary>
/// Result of downloading an executed workflow snapshot as a JSON file.
/// </summary>
public sealed class DownloadExecutedWorkflowResult
{
    /// <summary>
    /// UTF-8 encoded bytes of the pretty-printed Workflow JSON.
    /// </summary>
    public byte[] Content { get; init; } = [];

    /// <summary>
    /// MIME content type. Always <c>application/json</c>.
    /// </summary>
    public string ContentType { get; init; } = "application/json";

    /// <summary>
    /// Suggested file name (<c>{WorkflowName}.workflow</c>).
    /// </summary>
    public string FileName { get; init; } = "executed-workflow.workflow";
}

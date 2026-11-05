namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// Specifies the type of background job.
/// </summary>
public enum BackgroundJobType
{
    /// <summary>
    /// No specific job type.
    /// </summary>
    None = 0,

    /// <summary>
    /// A workflow execution job.
    /// </summary>
    WorkflowExecution = 2,

    /// <summary>
    /// A unit of in-process work that is not a workflow run, such as an editor agent turn.
    /// </summary>
    Delegated = 3,
}

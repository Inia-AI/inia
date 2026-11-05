namespace IniaAi.Domain.Entities.Workflows.Enums;

/// <summary>
/// Represents the current execution status of a workflow block.
/// </summary>
public enum ExecutionStatus
{
    /// <summary>
    /// The block has not been executed yet.
    /// </summary>
    NotExecuted = 0,

    /// <summary>
    /// The block is currently running.
    /// </summary>
    Running = 1,

    /// <summary>
    /// The block has completed successfully.
    /// </summary>
    Completed = 2,

    /// <summary>
    /// The block execution failed with an error.
    /// </summary>
    Failed = 3,

    /// <summary>
    /// The block was skipped because required conditions were not met.
    /// </summary>
    Skipped = 4
}
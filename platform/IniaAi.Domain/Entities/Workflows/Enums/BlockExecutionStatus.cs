namespace IniaAi.Domain.Entities.Workflows.Enums;

/// <summary>
/// Represents the status of a block after execution.
/// Values must match WebApi.Contracts.Enums.BlockExecutionStatus for proper mapping.
/// </summary>
public enum BlockExecutionStatus
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
    /// The block execution failed due to an exception or error.
    /// </summary>
    Failed = 3,

    /// <summary>
    /// The block was skipped (e.g., due to unmet conditions or required documents missing).
    /// </summary>
    Skipped = 4
}

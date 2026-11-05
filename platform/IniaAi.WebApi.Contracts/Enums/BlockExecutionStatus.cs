using System.Text.Json.Serialization;

namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// Represents the current execution status of a workflow block.
/// Values must match Domain.Entities.Workflows.Enums.ExecutionStatus for proper mapping.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
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
    /// The block execution failed with an error.
    /// </summary>
    Failed = 3,

    /// <summary>
    /// The block was skipped because required conditions were not met.
    /// </summary>
    Skipped = 4
}

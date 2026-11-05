using IniaAi.Common;
using IniaAi.Common.Reflection;
using IniaAi.Common.Serialization;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace IniaAi.Domain.Entities.Workflows.Blocks.ExecutionStates.Abstractions;

/// <summary>
/// Base class for block execution state tracking.
/// Execution states are stored as JSON in the Block table and rebuilt on each execution.
/// Contains typed Properties for configuration and legacy flat properties for backward compatibility.
/// </summary>
public class BlockExecutionState : JsonSerializable<BlockExecutionState>, IDisposable
{
    private bool _disposed = false;

    /// <summary>
    /// Current execution status of the block.
    /// </summary>
    [JsonProperty(nameof(Status))]
    [JsonConverter(typeof(StringEnumConverter))]
    public ExecutionStatus Status { get; set; } = ExecutionStatus.NotExecuted;

    /// <summary>
    /// Timestamp when the block execution started.
    /// </summary>
    [JsonProperty(nameof(StartedAt), NullValueHandling = NullValueHandling.Ignore)]
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// Timestamp when the block execution completed (success or failure).
    /// </summary>
    [JsonProperty(nameof(CompletedAt), NullValueHandling = NullValueHandling.Ignore)]
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Error message or exception details if execution failed or was skipped.
    /// </summary>
    [JsonProperty(nameof(Error), NullValueHandling = NullValueHandling.Ignore)]
    public string? Error
    {
        get;
        set => field = value.RemoveNullCharacters();
    }

    /// <summary>
    /// Output title template resolved with placeholders replaced. A blank title is none, so the
    /// report shows no empty subtitle and a citation falls back to the block's name.
    /// </summary>
    [JsonProperty(nameof(OutputTitle), NullValueHandling = NullValueHandling.Ignore)]
    public string? OutputTitle
    {
        get;
        set => field = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    [JsonProperty(nameof(Sources))]
    public List<string> Sources { get; set; } = [];

    public bool ShouldSerializeSources()
    {
        return Sources.Count > 0;
    }

    [JsonProperty(nameof(ConfidenceScore), NullValueHandling = NullValueHandling.Ignore)]
    public double? ConfidenceScore { get; set; }

    /// <summary>
    /// Parameterless constructor for JSON deserialization.
    /// </summary>
    [JsonConstructor]
    internal BlockExecutionState()
    {
    }

    /// <summary>
    /// Constructs execution state from block configuration.
    /// </summary>
    /// <param name="block">The block to initialize execution state from.</param>
    public BlockExecutionState(Block block)
    {
        OutputTitle = block.OutputTitleTemplate;
    }

    /// <summary>
    /// Replace placeholders in execution state templates.
    /// Can be overridden by derived classes to handle their specific fields.
    /// </summary>
    /// <param name="asPlaceHolder">Whether to format as placeholder (with braces) or direct replacement.</param>
    /// <param name="inputs">Dictionary of placeholder values to replace.</param>
    /// <param name="stringComparison">String comparison mode for placeholder matching.</param>
    public virtual void ReplaceAllPlaceholders(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        PlaceholderReflectionHelper.ReplaceAll(this, asPlaceHolder, inputs, stringComparison);
    }

    /// <summary>
    /// Marks the block execution as started.
    /// </summary>
    public void MarkAsStarted()
    {
        Status = ExecutionStatus.Running;
        StartedAt = DateTime.UtcNow;
        Error = null;
        CompletedAt = null;
    }

    /// <summary>
    /// Marks the block execution as completed successfully.
    /// </summary>
    public void MarkAsCompleted()
    {
        Status = ExecutionStatus.Completed;
        CompletedAt = DateTime.UtcNow;
        Error = null;
    }

    /// <summary>
    /// Marks the block execution as failed with an error.
    /// </summary>
    /// <param name="error">The error message or exception details.</param>
    public void MarkAsFailed(string error)
    {
        Status = ExecutionStatus.Failed;
        CompletedAt = DateTime.UtcNow;
        Error = error;
    }

    /// <summary>
    /// Marks the block execution as skipped due to missing required conditions.
    /// </summary>
    /// <param name="reason">The reason why the block was skipped.</param>
    public void MarkAsSkipped(string reason)
    {
        Status = ExecutionStatus.Skipped;
        CompletedAt = DateTime.UtcNow;
        Error = reason;
    }

    /// <summary>
    /// Gets the execution duration if the block has started.
    /// </summary>
    public TimeSpan? ExecutionDuration
    {
        get
        {
            if (StartedAt is null)
            {
                return null;
            }

            DateTime endTime = CompletedAt ?? DateTime.UtcNow;
            return endTime - StartedAt;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the block has failed.
    /// </summary>
    public bool HasFailed => Status is ExecutionStatus.Failed;

    /// <summary>
    /// Gets a value indicating whether the block was skipped.
    /// </summary>
    public bool IsSkipped => Status is ExecutionStatus.Skipped;

    /// <summary>
    /// Public Dispose method to clean up resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected Dispose method following the dispose pattern.
    /// Derived classes should override to dispose their specific resources.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed resources here.
                // Base class disposes common fields
                OutputTitle = null;
                Error = null;

                Sources.Clear();
            }

            _disposed = true;
        }
    }

    /// <summary>
    /// Destructor (Finalizer) to ensure disposal.
    /// </summary>
    ~BlockExecutionState()
    {
        Dispose(false);
    }
}

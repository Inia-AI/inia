namespace IniaAi.Core.Workflows.Graph;

/// <summary>
/// Service for querying workflow execution graphs and computing provenance.
/// </summary>
public interface IGraphQueryService
{
    /// <summary>
    /// Validates that the specified organization has access to the workflow execution.
    /// </summary>
    /// <param name="runId">The workflow execution run ID.</param>
    /// <param name="organizationId">The organization ID to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the organization has access; false otherwise.</returns>
    Task<bool> ValidateAccessAsync(
        string runId,
        string organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that a target tag exists in the workflow execution graph.
    /// </summary>
    /// <param name="runId">The workflow execution run ID.</param>
    /// <param name="targetTag">The ReplacementTag to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the target exists; false otherwise.</returns>
    Task<bool> ValidateTargetExistsAsync(
        string runId,
        string targetTag,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes upstream provenance for a target node.
    /// </summary>
    /// <param name="runId">The workflow execution run ID.</param>
    /// <param name="targetTag">The ReplacementTag of the target node.</param>
    /// <param name="question">The user's question.</param>
    /// <param name="startingDepth">Starting traversal depth (LLM may auto-expand up to maximum depth).</param>
    /// <param name="includePreviews">Whether to include output previews.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A provenance result with answer and metadata.</returns>
    Task<ProvenanceResult> QueryAsync(
        string runId,
        string targetTag,
        string question,
        int startingDepth = 4,
        bool includePreviews = true,
        CancellationToken cancellationToken = default);
}

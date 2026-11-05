using System.ComponentModel.DataAnnotations;

namespace IniaAi.WebApi.Contracts.Requests.Graph;

/// <summary>
/// Request payload for querying provenance-backed answers about a workflow execution field.
/// </summary>
public class GraphQueryRequest
{
    /// <summary>
    /// Gets or sets the ReplacementTag of the target report block to explain.
    /// </summary>
    [Required]
    public string TargetTag { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user's question about the target field.
    /// Example: "How did you calculate short-term debt?"
    /// </summary>
    [Required]
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the starting traversal depth for upstream provenance search.
    /// The LLM may automatically expand this up to 6 if more context is needed.
    /// Defaults to 4 if not specified. Valid range: 1-6.
    /// </summary>
    [Range(1, 6)]
    public int StartingDepth { get; set; } = 4;

    /// <summary>
    /// Gets or sets whether to include sanitized output previews in the provenance response.
    /// Defaults to true.
    /// </summary>
    public bool IncludePreviews { get; set; } = true;
}

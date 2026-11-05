namespace IniaAi.WebApi.Models.Responses.ContentRefinement;

/// <summary>
/// Represents the result of a content refinement operation.
/// When <see cref="IsOutOfScope"/> is true, <see cref="RefinedContent"/> and <see cref="Explanation"/> are null.
/// </summary>
public class RefineContentResponse
{
    /// <summary>
    /// Gets or sets the refined content after AI processing.
    /// </summary>
    public string? RefinedContent { get; set; }

    /// <summary>
    /// Gets or sets the AI's explanation of what changes were made and why.
    /// </summary>
    public string? Explanation { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the request fell outside the refinement feature's scope.
    /// </summary>
    public bool IsOutOfScope { get; set; }
}

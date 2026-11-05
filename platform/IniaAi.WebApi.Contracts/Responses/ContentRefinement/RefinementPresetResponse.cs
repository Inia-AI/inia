namespace IniaAi.WebApi.Models.Responses.ContentRefinement;

/// <summary>
/// Response DTO for a refinement instruction preset.
/// </summary>
public class RefinementPresetResponse
{
    /// <summary>
    /// Gets or sets the unique identifier of the preset.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Gets or sets the display name of the preset.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the refinement instruction text.
    /// </summary>
    public required string Instruction { get; set; }

    /// <summary>
    /// Gets or sets whether the preset is shared with the organization.
    /// </summary>
    public bool IsShared { get; set; }

    /// <summary>
    /// Gets or sets the ID of the user who created the preset.
    /// </summary>
    public required string UserId { get; set; }

    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}

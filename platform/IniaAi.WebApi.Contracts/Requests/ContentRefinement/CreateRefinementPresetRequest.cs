namespace IniaAi.WebApi.Models.Requests.ContentRefinement;

/// <summary>
/// Request to create a new refinement instruction preset.
/// </summary>
public class CreateRefinementPresetRequest
{
    /// <summary>
    /// Display name for the preset.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The refinement instruction text.
    /// </summary>
    public required string Instruction { get; set; }

    /// <summary>
    /// When true, the preset is visible to all users in the organization.
    /// </summary>
    public bool IsShared { get; set; } = false;
}

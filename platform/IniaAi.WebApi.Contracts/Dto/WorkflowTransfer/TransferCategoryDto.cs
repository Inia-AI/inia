namespace IniaAi.WebApi.Contracts.Dto.WorkflowTransfer;

/// <summary>
/// A shared-component category carried in a transfer bundle as identity plus display. On import it
/// is upserted only when absent; on an id collision the local name and description win.
/// </summary>
/// <param name="Id">The category's stable identifier, shared across deployments.</param>
/// <param name="Name">The category's display name on the origin deployment.</param>
/// <param name="Description">The category's description on the origin deployment, when set.</param>
public record TransferCategoryDto(
    string Id,
    string Name,
    string? Description);

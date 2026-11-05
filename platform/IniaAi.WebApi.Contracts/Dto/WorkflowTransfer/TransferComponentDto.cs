using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Dto.WorkflowTransfer;

/// <summary>
/// One shared component the bundle's template depends on, carried at its current head plus the
/// fingerprint trail of its past versions. The trail is what the target uses to work out whether
/// its own copy of the component is the same one, ahead, behind, or diverged — never the id.
/// </summary>
/// <param name="ComponentId">
/// The component's identifier on the origin deployment. It is the bundle-local key that component
/// decisions reference, and the id the target adopts when it creates the component fresh; it is
/// never read as proof of identity on its own.
/// </param>
/// <param name="Kind">Whether the component is a block or a section.</param>
/// <param name="Name">The component's display name on the origin deployment.</param>
/// <param name="Description">The component's description on the origin deployment, when set.</param>
/// <param name="CategoryId">The category the component files under, or null when uncategorized.</param>
/// <param name="HeadContentHash">The content hash of the head definition carried below.</param>
/// <param name="HeadDefinitionJson">The head version's stored definition payload, verbatim.</param>
/// <param name="HashTrail">The content hashes of the component's versions, oldest first through the head.</param>
public record TransferComponentDto(
    string ComponentId,
    SharedComponentKind Kind,
    string Name,
    string? Description,
    string? CategoryId,
    string HeadContentHash,
    string HeadDefinitionJson,
    List<string> HashTrail);

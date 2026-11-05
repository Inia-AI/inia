using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Responses.SharedComponents;

/// <summary>
/// A shared component's registry entry with its current head version tokens.
/// </summary>
/// <param name="Id">The component identifier.</param>
/// <param name="Kind">Whether the component is a block or a section.</param>
/// <param name="Name">The component's display name.</param>
/// <param name="Description">The component's description, when set.</param>
/// <param name="CategoryId">The curated category the component files under, or null for uncategorized legacy rows.</param>
/// <param name="CategoryName">The display name resolved for <paramref name="CategoryId"/>, or null when unset or unresolvable.</param>
/// <param name="State">The component's lifecycle state.</param>
/// <param name="HeadVersionId">The current head version identifier, or null when no version exists yet.</param>
/// <param name="HeadContentHash">The content hash of the head version, when available.</param>
/// <param name="HeadCreatedAtUtc">When the head version was created, or null when no version exists yet.</param>
/// <param name="ConsumerCount">How many live template copies are currently linked to this component.</param>
/// <param name="ConsumingWorkflowTypes">The workflow types holding those linked copies, ordered by display name.</param>
/// <param name="ViaSectionConsumerCount">
/// How many of <paramref name="ConsumerCount"/>'s copies reach this component indirectly, as ref
/// entries of linked section copies rather than as directly linked blocks.
/// </param>
/// <param name="BrokenOutInstanceCount">
/// How many copies have broken out of this component. They keep the component id as provenance but
/// no longer track it, so they are deliberately excluded from <paramref name="ConsumerCount"/>; a
/// definition change never reaches them.
/// </param>
public record SharedComponentSummaryDto(
    string Id,
    SharedComponentKind Kind,
    string Name,
    string? Description,
    string? CategoryId,
    string? CategoryName,
    SharedComponentState State,
    string? HeadVersionId,
    string? HeadContentHash,
    DateTime? HeadCreatedAtUtc,
    int ConsumerCount,
    List<ConsumingWorkflowTypeDto> ConsumingWorkflowTypes,
    int ViaSectionConsumerCount = 0,
    int BrokenOutInstanceCount = 0);

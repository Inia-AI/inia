using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Responses.SharedComponents;

/// <summary>
/// One workflow type holding live linked copies of a shared component.
/// </summary>
/// <param name="WorkflowTypeId">The consuming workflow type's identifier.</param>
/// <param name="WorkflowTypeName">The consuming workflow type's display name, falling back to the identifier when no type row resolves.</param>
/// <param name="LinkedInstanceCount">How many live linked copies of the component the type holds.</param>
/// <param name="CategoryId">The workflow-type category the consuming type files under, or null when uncategorized.</param>
/// <param name="CategoryName">The display name resolved for <paramref name="CategoryId"/>, or null when unset.</param>
/// <param name="Channel">Whether the type holds the component directly or reaches it through a section component.</param>
/// <param name="ViaSharedComponentId">For a <see cref="SharedComponentConsumerChannel.ViaSection"/> row, the section component that references this one; null otherwise.</param>
/// <param name="ViaSharedComponentName">The display name of <paramref name="ViaSharedComponentId"/>, or null when unset.</param>
public record ConsumingWorkflowTypeDto(
    string WorkflowTypeId,
    string WorkflowTypeName,
    int LinkedInstanceCount,
    string? CategoryId,
    string? CategoryName,
    SharedComponentConsumerChannel Channel = SharedComponentConsumerChannel.Direct,
    string? ViaSharedComponentId = null,
    string? ViaSharedComponentName = null);

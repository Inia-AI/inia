using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using ContractsKind = IniaAi.WebApi.Contracts.Enums.SharedComponentKind;

namespace IniaAi.Application.Commands.SharedComponents.PromoteSharedComponent;

/// <summary>
/// Turns an existing template block or section into a Library component and links the instance,
/// or attaches the instance to an existing component when its normalized content matches that
/// component's head exactly (<paramref name="AttachToComponentId"/>). The target is addressed by
/// canonical node path (<c>Sections[i]</c> or <c>Sections[i].Blocks[j]</c>) or by stable id —
/// exactly one of the two. The template save enforces optimistic concurrency via
/// <paramref name="BaseVersionId"/>. New components are born categorized:
/// <paramref name="CategoryId"/> is required on the create path and must name an existing
/// vocabulary entry, while the attach path forbids it — the existing component keeps its category.
/// When the target block sits inside a Linked section, promoting it rewrites that section
/// component's slot from inline to a reference and fans the change out; the container append is
/// guarded by <paramref name="ExpectedContainerHeadVersionId"/>, required exactly in that case.
/// </summary>
public record PromoteSharedComponentCommand(
    string OrganizationId,
    string UserId,
    string WorkflowTypeId,
    string? TargetPath = null,
    string? TargetStableId = null,
    string? Name = null,
    string? Description = null,
    string? AttachToComponentId = null,
    string? BaseVersionId = null,
    string? CategoryId = null,
    string? ExpectedContainerHeadVersionId = null) : IRequest<ErrorOr<PromoteSharedComponentResult>>;

public record PromoteSharedComponentResult(
    string SharedComponentId,
    string Name,
    ContractsKind Kind,
    string ComponentVersionHash,
    bool WasAttached,
    string TargetPath,
    string VersionId,
    string ContentHash,
    bool WasDeduped);

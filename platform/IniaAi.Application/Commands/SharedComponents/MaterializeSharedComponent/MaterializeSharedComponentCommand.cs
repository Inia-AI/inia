using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;

namespace IniaAi.Application.Commands.SharedComponents.MaterializeSharedComponent;

/// <summary>
/// Inserts a linked copy of a Library component into a template. The server performs the whole
/// materialization — definition copy, fresh stable ids, tag-collision rewrite, bindings, and link
/// stamping; callers never hand-write link fields. Block components land in the section addressed
/// by <paramref name="TargetSectionPath"/> or <paramref name="TargetSectionStableId"/> (exactly
/// one); section components land at <paramref name="Index"/> in the workflow and take no section
/// locator. The template save enforces optimistic concurrency via <paramref name="BaseVersionId"/>.
/// </summary>
public record MaterializeSharedComponentCommand(
    string OrganizationId,
    string UserId,
    string WorkflowTypeId,
    string SharedComponentId,
    string? TargetSectionPath = null,
    string? TargetSectionStableId = null,
    int? Index = null,
    string? ReplacementTag = null,
    string? BaseVersionId = null) : IRequest<ErrorOr<MaterializeSharedComponentResult>>;

public record MaterializeSharedComponentResult(
    string SharedComponentId,
    string ComponentVersionHash,
    string LandedPath,
    string? LandedReplacementTag,
    IReadOnlyDictionary<string, string>? Bindings,
    string VersionId,
    string ContentHash,
    bool WasDeduped,
    IReadOnlyList<WorkflowStructuralValidationWarning> Warnings);

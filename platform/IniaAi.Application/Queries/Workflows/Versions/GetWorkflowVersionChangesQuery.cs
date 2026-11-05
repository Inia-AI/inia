using ErrorOr;
using FluentValidation;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Core.Workflows.Diff;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Specifications.Workflows;

namespace IniaAi.Application.Queries.Workflows.Versions;

public enum WorkflowVersionChangeStatus
{
    /// <summary>A structured diff against the parent version is available.</summary>
    Diffed,

    /// <summary>The version has no parent — it is the first save of its type.</summary>
    FirstVersion,

    /// <summary>The version's or its parent's payload was trimmed, so no diff can be computed.</summary>
    Unavailable,
}

/// <summary>
/// The changes one version introduced relative to its parent, resolved server-side so callers
/// never handle <c>ParentVersionId</c>. <see cref="StoredNarrative"/> is always populated from
/// the version row and is the fallback display when <see cref="Diff"/> is unavailable.
/// </summary>
public record WorkflowVersionChangesResult(
    WorkflowVersionChangeStatus Status,
    WorkflowDiff? Diff,
    string? StoredNarrative)
{
    public string VersionId { get; init; } = string.Empty;

    public string? ParentVersionId { get; init; }
}

/// <remarks>
/// Deliberately not gated by WorkflowTypeOrganizationAccess: it serves the role-gated authoring
/// surfaces (the workflow editor and the EDITOR_ROLES template API), which author universal
/// templates whose types may lack access rows entirely. Any HTTP mouth must carry that role gate.
/// A non-null <c>WorkflowTypeId</c> scopes the read to that type: a version of another type
/// answers as not found. <c>FailWhenUnavailable</c> answers an undiffable version with the payload
/// error instead of <see cref="WorkflowVersionChangeStatus.Unavailable"/>, for callers with no
/// narrative fallback to show.
/// </remarks>
public record GetWorkflowVersionChangesQuery(
    string OrganizationId,
    string VersionId,
    string? WorkflowTypeId = null,
    bool FailWhenUnavailable = false)
    : IRequest<ErrorOr<WorkflowVersionChangesResult>>;

public class GetWorkflowVersionChangesQueryHandler
    : IRequestHandler<GetWorkflowVersionChangesQuery, ErrorOr<WorkflowVersionChangesResult>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly WorkflowDiffService _diffService;

    public GetWorkflowVersionChangesQueryHandler(
        ICoreUnitOfWork coreUnitOfWork,
        WorkflowDiffService diffService)
    {
        _coreUnitOfWork = coreUnitOfWork;
        _diffService = diffService;
    }

    public async Task<ErrorOr<WorkflowVersionChangesResult>> Handle(
        GetWorkflowVersionChangesQuery request,
        CancellationToken cancellationToken)
    {
        WorkflowVersion? version = await _coreUnitOfWork.Repository<WorkflowVersion>()
            .GetFirstOrDefaultAsync(
                new WorkflowVersionByIdSpec(request.VersionId),
                cancellationToken);
        if (version is null || WorkflowVersionErrors.IsOutOfScope(version, request.WorkflowTypeId))
        {
            return WorkflowVersionErrors.NotFound(request.VersionId, request.WorkflowTypeId);
        }

        if (version.ParentVersionId is null)
        {
            return Result(WorkflowVersionChangeStatus.FirstVersion, diff: null, version);
        }

        Error? problem = WorkflowVersionErrors.PayloadProblem(version);
        WorkflowVersion? parent = null;
        if (problem is null)
        {
            parent = await _coreUnitOfWork.Repository<WorkflowVersion>()
                .GetFirstOrDefaultAsync(
                    new WorkflowVersionByIdSpec(version.ParentVersionId),
                    cancellationToken);
            problem = parent is null
                ? WorkflowVersionErrors.NotFound(version.ParentVersionId, request.WorkflowTypeId)
                : WorkflowVersionErrors.PayloadProblem(parent);
        }

        if (problem is not null || parent is null)
        {
            return request.FailWhenUnavailable && problem is Error error
                ? error
                : Result(WorkflowVersionChangeStatus.Unavailable, diff: null, version);
        }

        WorkflowDiff diff = _diffService.Compare(parent.WorkflowJson, version.WorkflowJson);

        return Result(WorkflowVersionChangeStatus.Diffed, diff, version);
    }

    private static WorkflowVersionChangesResult Result(
        WorkflowVersionChangeStatus status,
        WorkflowDiff? diff,
        WorkflowVersion version)
    {
        return new WorkflowVersionChangesResult(status, diff, version.AutoNarrativeSummary)
        {
            VersionId = version.Id,
            ParentVersionId = version.ParentVersionId,
        };
    }
}

public class GetWorkflowVersionChangesQueryValidator : AbstractValidator<GetWorkflowVersionChangesQuery>
{
    public GetWorkflowVersionChangesQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.VersionId).NotEmpty();
        RuleFor(x => x.WorkflowTypeId).NotEmpty().When(x => x.WorkflowTypeId is not null);
    }
}

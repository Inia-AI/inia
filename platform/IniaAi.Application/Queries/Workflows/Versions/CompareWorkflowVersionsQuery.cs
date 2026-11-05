using ErrorOr;
using FluentValidation;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Core.Workflows.Diff;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Specifications.Workflows;

namespace IniaAi.Application.Queries.Workflows.Versions;

/// <remarks>
/// Deliberately not gated by WorkflowTypeOrganizationAccess: it serves the role-gated authoring
/// surfaces (the workflow editor and the EDITOR_ROLES template API), which author universal
/// templates whose types may lack access rows entirely. Any HTTP mouth must carry that role gate.
/// A non-null <c>WorkflowTypeId</c> scopes the read to that type: a version of another type
/// answers as not found. <c>PreserveArgumentOrder</c> diffs A to B exactly as given; without it
/// the diff always reads older to newer.
/// </remarks>
public record CompareWorkflowVersionsQuery(
    string OrganizationId,
    string VersionIdA,
    string VersionIdB,
    string? WorkflowTypeId = null,
    bool PreserveArgumentOrder = false)
    : IRequest<ErrorOr<WorkflowDiff>>;

public class CompareWorkflowVersionsQueryHandler
    : IRequestHandler<CompareWorkflowVersionsQuery, ErrorOr<WorkflowDiff>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly WorkflowDiffService _diffService;

    public CompareWorkflowVersionsQueryHandler(
        ICoreUnitOfWork coreUnitOfWork,
        WorkflowDiffService diffService)
    {
        _coreUnitOfWork = coreUnitOfWork;
        _diffService = diffService;
    }

    public async Task<ErrorOr<WorkflowDiff>> Handle(
        CompareWorkflowVersionsQuery request,
        CancellationToken cancellationToken)
    {
        WorkflowVersion? versionA = await _coreUnitOfWork.Repository<WorkflowVersion>()
            .GetFirstOrDefaultAsync(
                new WorkflowVersionByIdSpec(request.VersionIdA),
                cancellationToken);
        WorkflowVersion? versionB = await _coreUnitOfWork.Repository<WorkflowVersion>()
            .GetFirstOrDefaultAsync(
                new WorkflowVersionByIdSpec(request.VersionIdB),
                cancellationToken);

        if (versionA is null || WorkflowVersionErrors.IsOutOfScope(versionA, request.WorkflowTypeId))
        {
            return WorkflowVersionErrors.NotFound(request.VersionIdA, request.WorkflowTypeId);
        }

        if (versionB is null || WorkflowVersionErrors.IsOutOfScope(versionB, request.WorkflowTypeId))
        {
            return WorkflowVersionErrors.NotFound(request.VersionIdB, request.WorkflowTypeId);
        }

        if ((WorkflowVersionErrors.PayloadProblem(versionA) ?? WorkflowVersionErrors.PayloadProblem(versionB)) is Error payloadProblem)
        {
            return payloadProblem;
        }

        (WorkflowVersion before, WorkflowVersion after) =
            request.PreserveArgumentOrder || versionA.CreatedAt <= versionB.CreatedAt
                ? (versionA, versionB)
                : (versionB, versionA);

        return _diffService.Compare(before.WorkflowJson, after.WorkflowJson);
    }
}

public class CompareWorkflowVersionsQueryValidator : AbstractValidator<CompareWorkflowVersionsQuery>
{
    public CompareWorkflowVersionsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.VersionIdA).NotEmpty();
        RuleFor(x => x.VersionIdB).NotEmpty();
        RuleFor(x => x.WorkflowTypeId).NotEmpty().When(x => x.WorkflowTypeId is not null);
    }
}

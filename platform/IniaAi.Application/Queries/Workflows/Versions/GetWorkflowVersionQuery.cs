using ErrorOr;
using FluentValidation;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Specifications.Workflows;
using IniaAi.WebApi.Contracts.Responses.WorkflowVersions;

namespace IniaAi.Application.Queries.Workflows.Versions;

/// <remarks>
/// Deliberately not gated by WorkflowTypeOrganizationAccess: it serves the role-gated authoring
/// surfaces (the workflow editor and the EDITOR_ROLES template API), which author universal
/// templates whose types may lack access rows entirely. Any HTTP mouth must carry that role gate.
/// A non-null <c>WorkflowTypeId</c> scopes the read to that type: a version of another type
/// answers as not found.
/// </remarks>
public record GetWorkflowVersionQuery(string OrganizationId, string VersionId, string? WorkflowTypeId = null)
    : IRequest<ErrorOr<WorkflowVersionContentDto>>;

public class GetWorkflowVersionQueryHandler
    : IRequestHandler<GetWorkflowVersionQuery, ErrorOr<WorkflowVersionContentDto>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;

    public GetWorkflowVersionQueryHandler(ICoreUnitOfWork coreUnitOfWork)
    {
        _coreUnitOfWork = coreUnitOfWork;
    }

    public async Task<ErrorOr<WorkflowVersionContentDto>> Handle(
        GetWorkflowVersionQuery request,
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

        if (WorkflowVersionErrors.PayloadProblem(version) is Error payloadProblem)
        {
            return payloadProblem;
        }

        return new WorkflowVersionContentDto(
            version.Id,
            version.WorkflowTypeId,
            version.ContentHash,
            WorkflowVersionCode.Short(version.ContentHash),
            version.WorkflowJson,
            version.CreatedAt,
            version.CreatedByUserId,
            version.AutoNarrativeSummary)
        {
            ParentVersionId = version.ParentVersionId,
        };
    }
}

public class GetWorkflowVersionQueryValidator : AbstractValidator<GetWorkflowVersionQuery>
{
    public GetWorkflowVersionQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.VersionId).NotEmpty();
        RuleFor(x => x.WorkflowTypeId).NotEmpty().When(x => x.WorkflowTypeId is not null);
    }
}

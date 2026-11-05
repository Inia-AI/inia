using ErrorOr;
using FluentValidation;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Specifications.Workflows;

namespace IniaAi.Application.Queries.Workflows.Versions;

/// <remarks>
/// Deliberately not gated by WorkflowTypeOrganizationAccess: it serves the role-gated workflow
/// editor's copy-to-Core flow, which authors universal templates whose types may lack access rows
/// entirely. Any HTTP mouth must carry the EDITOR_ROLES gate the template API uses.
/// </remarks>
public record GetWorkflowHeadVersionQuery(string OrganizationId, string WorkflowTypeId)
    : IRequest<ErrorOr<WorkflowHeadVersionResult>>;

public record WorkflowHeadVersionResult(string? VersionId);

public class GetWorkflowHeadVersionQueryHandler
    : IRequestHandler<GetWorkflowHeadVersionQuery, ErrorOr<WorkflowHeadVersionResult>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;

    public GetWorkflowHeadVersionQueryHandler(ICoreUnitOfWork coreUnitOfWork)
    {
        _coreUnitOfWork = coreUnitOfWork;
    }

    public async Task<ErrorOr<WorkflowHeadVersionResult>> Handle(
        GetWorkflowHeadVersionQuery request,
        CancellationToken cancellationToken)
    {
        string? headVersionId = await _coreUnitOfWork.Repository<WorkflowVersion>()
            .GetFirstOrDefaultAsync(
                new LatestWorkflowVersionIdByTypeSpec(request.WorkflowTypeId),
                cancellationToken);

        return new WorkflowHeadVersionResult(headVersionId);
    }
}

public class GetWorkflowHeadVersionQueryValidator : AbstractValidator<GetWorkflowHeadVersionQuery>
{
    public GetWorkflowHeadVersionQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.WorkflowTypeId).NotEmpty();
    }
}

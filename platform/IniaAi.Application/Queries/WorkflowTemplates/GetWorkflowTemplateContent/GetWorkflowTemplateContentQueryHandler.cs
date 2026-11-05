using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Serialization;
using IniaAi.Domain.Specifications.Workflows;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Queries.WorkflowTemplates.GetWorkflowTemplateContent;

/// <summary>
/// Handles <see cref="GetWorkflowTemplateContentQuery"/>. Serves the head
/// <see cref="WorkflowVersion"/> payload when available; falls back to canonicalizing the
/// current workflow row for legacy types without version history (or with trimmed payloads),
/// in which case <c>VersionId</c> is null and a save must omit <c>baseVersionId</c>.
/// </summary>
public class GetWorkflowTemplateContentQueryHandler
    : IRequestHandler<GetWorkflowTemplateContentQuery, ErrorOr<WorkflowTemplateContentResult>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly WorkflowCanonicalizer _canonicalizer;

    public GetWorkflowTemplateContentQueryHandler(
        ICoreUnitOfWork coreUnitOfWork,
        WorkflowCanonicalizer canonicalizer)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
        _canonicalizer = canonicalizer ?? throw new ArgumentNullException(nameof(canonicalizer));
    }

    public async Task<ErrorOr<WorkflowTemplateContentResult>> Handle(
        GetWorkflowTemplateContentQuery request,
        CancellationToken cancellationToken)
    {
        bool typeExists = await _coreUnitOfWork.WorkflowTypes
            .GetAll()
            .AsNoTracking()
            .AnyAsync(wt => wt.Id == request.WorkflowTypeId, cancellationToken);

        if (!typeExists)
        {
            return Error.NotFound(
                "WorkflowTemplate.NotFound",
                $"Workflow template '{request.WorkflowTypeId}' was not found.");
        }

        WorkflowVersion? head = await _coreUnitOfWork.Repository<WorkflowVersion>()
            .GetFirstOrDefaultAsync(
                new LatestWorkflowVersionByTypeSpec(request.WorkflowTypeId),
                cancellationToken);

        if (head is not null && head.PayloadTrimmedAt is null)
        {
            return new WorkflowTemplateContentResult(
                request.WorkflowTypeId,
                head.Id,
                head.ContentHash,
                head.CreatedAt,
                head.CreatedByUserId,
                head.WorkflowJson);
        }

        // No head, or the head payload was trimmed for retention: serve the current
        // workflow row instead. Head metadata (when present) is still returned so the
        // caller can pass the correct baseVersionId on save.
        Workflow? workflow = await _coreUnitOfWork.Workflows
            .GetByTypeIdAsNoTrackingAsync(request.WorkflowTypeId);

        if (workflow is null)
        {
            return Error.NotFound(
                "WorkflowTemplate.NoContent",
                $"Workflow template '{request.WorkflowTypeId}' has no content yet.");
        }

        return new WorkflowTemplateContentResult(
            request.WorkflowTypeId,
            head?.Id,
            head?.ContentHash,
            head?.CreatedAt,
            head?.CreatedByUserId,
            _canonicalizer.ToCanonicalJson(workflow));
    }
}

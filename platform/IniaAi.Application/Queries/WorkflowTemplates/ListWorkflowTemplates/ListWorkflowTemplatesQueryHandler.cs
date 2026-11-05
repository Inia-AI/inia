using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.WebApi.Contracts.Responses.WorkflowTemplates;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Queries.WorkflowTemplates.ListWorkflowTemplates;

/// <summary>
/// Handles <see cref="ListWorkflowTemplatesQuery"/> with a flat projection over workflow types,
/// including types that do not have a current workflow row yet (unlike the category queries).
/// </summary>
public class ListWorkflowTemplatesQueryHandler
    : IRequestHandler<ListWorkflowTemplatesQuery, ErrorOr<List<WorkflowTemplateSummaryDto>>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;

    public ListWorkflowTemplatesQueryHandler(ICoreUnitOfWork coreUnitOfWork)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
    }

    public async Task<ErrorOr<List<WorkflowTemplateSummaryDto>>> Handle(
        ListWorkflowTemplatesQuery request,
        CancellationToken cancellationToken)
    {
        List<WorkflowTemplateSummaryDto> templates = await _coreUnitOfWork.WorkflowTypes
            .GetAll()
            .AsNoTracking()
            .Where(WorkflowType.UserFacing)
            .OrderBy(wt => wt.Name)
            .Select(wt => new WorkflowTemplateSummaryDto(
                wt.Id,
                wt.Name,
                wt.Description,
                wt.CategoryId,
                wt.Category != null ? wt.Category.Name : null,
                wt.Workflows.Any()))
            .ToListAsync(cancellationToken);

        return templates;
    }
}

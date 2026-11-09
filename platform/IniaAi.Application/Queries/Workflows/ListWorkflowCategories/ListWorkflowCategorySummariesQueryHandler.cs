using ErrorOr;
using IniaAi.Application.Common.CreditCosts;
using IniaAi.Application.Common.RunLimits;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Dto;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Queries.Workflows.ListWorkflowCategories;

/// <summary>
/// Handles the query to retrieve lightweight workflow category summaries.
/// Projects only the columns the summaries show (<see cref="WorkflowSummaryProjections.ToListRow"/>),
/// then maps them in memory, because the credit cost sits inside the configuration column.
/// </summary>
public class ListWorkflowCategorySummariesQueryHandler
    : IRequestHandler<ListWorkflowCategorySummariesQuery, ErrorOr<List<WorkflowCategorySummaryDto>>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly IWorkflowCreditCost _creditCost;
    private readonly IWorkflowPageLimit _pageLimit;

    public ListWorkflowCategorySummariesQueryHandler(
        ICoreUnitOfWork coreUnitOfWork,
        IUserDataUnitOfWork userDataUnitOfWork,
        IWorkflowCreditCost creditCost,
        IWorkflowPageLimit pageLimit)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
        _userDataUnitOfWork = userDataUnitOfWork ?? throw new ArgumentNullException(nameof(userDataUnitOfWork));
        _creditCost = creditCost ?? throw new ArgumentNullException(nameof(creditCost));
        _pageLimit = pageLimit ?? throw new ArgumentNullException(nameof(pageLimit));
    }

    public async Task<ErrorOr<List<WorkflowCategorySummaryDto>>> Handle(
        ListWorkflowCategorySummariesQuery request,
        CancellationToken cancellationToken)
    {
        List<string> chosenTypeIds = await UserWorkflowTypeVisibilityResolver.ResolveChosenTypeIdsAsync(
            _userDataUnitOfWork, request.UserId, cancellationToken);

        bool hasChosenTypes = chosenTypeIds.Count > 0;

        // Exclude the internal-only workflow types (Generic, TestWorkflowType)
        List<CategoryRow> rows = await _coreUnitOfWork.Repository<WorkflowTypeCategory>()
            .GetAll()
            .AsNoTracking()
            .Where(c => c.WorkflowTypes!.Any(wt =>
                wt.OrganizationAccess.Any(oa => oa.OrganizationId == request.OrganizationId)
                && (!hasChosenTypes || chosenTypeIds.Contains(wt.Id))))
            .OrderBy(c => c.DisplayOrder)
            .Select(c => new CategoryRow
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                DisplayOrder = c.DisplayOrder,
                Workflows = c.WorkflowTypes!
                    .Where(wt => wt.OrganizationAccess.Any(oa => oa.OrganizationId == request.OrganizationId)
                        && (!hasChosenTypes || chosenTypeIds.Contains(wt.Id)))
                    .SelectMany(wt => wt.Workflows)
                    .Where(w => w.WorkflowTypeId != WorkflowType.GENERIC &&
                        w.WorkflowTypeId != WorkflowType.TEST_WORKFLOW_TYPE)
                    .AsQueryable()
                    .Select(WorkflowSummaryProjections.ToListRow)
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        List<WorkflowCategorySummaryDto> result = [.. rows.Select(row => new WorkflowCategorySummaryDto
        {
            Id = row.Id,
            Name = row.Name,
            Description = row.Description,
            DisplayOrder = row.DisplayOrder,
            Workflows = [.. row.Workflows.Select(workflow => WorkflowSummaryProjections.ToSummaryDto(workflow, _creditCost, _pageLimit))],
        })];

        return result;
    }

    private sealed class CategoryRow
    {
        public required string Id { get; init; }

        public required string Name { get; init; }

        public string? Description { get; init; }

        public int? DisplayOrder { get; init; }

        public required List<WorkflowSummaryProjections.WorkflowListRow> Workflows { get; init; }
    }
}

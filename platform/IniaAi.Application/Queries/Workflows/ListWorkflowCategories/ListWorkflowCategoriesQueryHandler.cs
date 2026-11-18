using ErrorOr;
using IniaAi.Application.Common.CreditCosts;
using IniaAi.Application.Common.RunLimits;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.WebApi.Contracts.Dto;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Queries.Workflows.ListWorkflowCategories;

/// <summary>
/// Handles the query to retrieve the workflow categories an organization can use, each with its workflows on request.
/// </summary>
public class ListWorkflowCategoriesQueryHandler
    : IRequestHandler<ListWorkflowCategoriesQuery, ErrorOr<List<WorkflowCategoryDto>>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly WorkflowTypeAccess _access;
    private readonly IWorkflowCreditCost _creditCost;
    private readonly IWorkflowPageLimit _pageLimit;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListWorkflowCategoriesQueryHandler"/> class.
    /// </summary>
    /// <param name="coreUnitOfWork">The unit of work for core repository access.</param>
    /// <param name="access">Decides which workflow types the organization reaches.</param>
    /// <param name="creditCost">Reads the credit cost of each workflow type.</param>
    /// <param name="pageLimit">Reads the page limit of each workflow type.</param>
    /// <exception cref="ArgumentNullException">Thrown if any parameter is null.</exception>
    public ListWorkflowCategoriesQueryHandler(
        ICoreUnitOfWork coreUnitOfWork,
        WorkflowTypeAccess access,
        IWorkflowCreditCost creditCost,
        IWorkflowPageLimit pageLimit)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _creditCost = creditCost ?? throw new ArgumentNullException(nameof(creditCost));
        _pageLimit = pageLimit ?? throw new ArgumentNullException(nameof(pageLimit));
    }

    /// <summary>
    /// Handles the incoming request to return the organization's workflow categories.
    /// </summary>
    /// <param name="request">The request naming the organization and whether to include workflows.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>The categories in display order.</returns>
    public async Task<ErrorOr<List<WorkflowCategoryDto>>> Handle(ListWorkflowCategoriesQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<string> reachable = await _access.TypeIdsAsync(request.OrganizationId, cancellationToken);
        IQueryable<WorkflowTypeCategory> categories = _coreUnitOfWork.Repository<WorkflowTypeCategory>()
            .GetAll()
            .AsNoTracking()
            .Where(category => category.WorkflowTypes.Any(type => reachable.Contains(type.Id)))
            .OrderBy(category => category.DisplayOrder);

        if (!request.IncludeWorkflows)
        {
            List<WorkflowCategoryDto> bare = await categories
                .Select(category => new WorkflowCategoryDto { Id = category.Id, Name = category.Name, Description = category.Description })
                .ToListAsync(cancellationToken);
            return bare;
        }

        List<CategoryParts> parts = await categories
            .Select(category => new CategoryParts(
                category.Id,
                category.Name,
                category.Description,
                category.WorkflowTypes
                    .Where(type => reachable.Contains(type.Id))
                    .SelectMany(type => type.Workflows)
                    .AsQueryable()
                    .Select(WorkflowSummaryProjections.ToParts)
                    .ToList()))
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        List<WorkflowCategoryDto> result = [.. parts.Select(ToDto)];
        return result;
    }

    private WorkflowCategoryDto ToDto(CategoryParts category)
    {
        return new WorkflowCategoryDto
        {
            Id = category.Id,
            Name = category.Name,
            Description = category.Description,
            Workflows = category.Workflows.Count == 0 ? null : [.. category.Workflows.Select(workflow => WorkflowSummaryProjections.ToDto(workflow, _creditCost, _pageLimit))],
        };
    }

    private sealed record CategoryParts(
        string Id,
        string Name,
        string? Description,
        List<WorkflowSummaryProjections.WorkflowParts> Workflows);
}

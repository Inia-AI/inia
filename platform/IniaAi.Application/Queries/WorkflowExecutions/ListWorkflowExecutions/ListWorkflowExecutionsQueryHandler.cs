using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.OData;
using IniaAi.Application.Common.Workflows;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Specifications.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;

namespace IniaAi.Application.Queries.WorkflowExecutions.ListWorkflowExecutions;

/// <summary>
/// Handles the query to retrieve a paginated and OData-compliant list of <see cref="WorkflowExecutionSummaryDto"/>.
/// </summary>
public class ListWorkflowExecutionsQueryHandler
    : IRequestHandler<ListWorkflowExecutionsQuery, ErrorOr<ODataPaginatedResponse<WorkflowExecutionSummaryDto>>>
{
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly IBaselineReader _baselines;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListWorkflowExecutionsQueryHandler"/> class.
    /// </summary>
    /// <param name="userDataUnitOfWork">The unit of work for user data repository access.</param>
    /// <param name="baselines">Reads whether each starred row's baseline is verified.</param>
    /// <exception cref="ArgumentNullException">Thrown if any parameter is null.</exception>
    public ListWorkflowExecutionsQueryHandler(
        IUserDataUnitOfWork userDataUnitOfWork,
        IBaselineReader baselines)
    {
        _userDataUnitOfWork = userDataUnitOfWork ?? throw new ArgumentNullException(nameof(userDataUnitOfWork));
        _baselines = baselines ?? throw new ArgumentNullException(nameof(baselines));
    }

    /// <summary>
    /// Handles the incoming request to return paginated workflow outputs.
    /// </summary>
    /// <param name="request">The request containing query options.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>A paginated OData response containing workflow output DTOs.</returns>
    public async Task<ErrorOr<ODataPaginatedResponse<WorkflowExecutionSummaryDto>>> Handle(ListWorkflowExecutionsQuery request, CancellationToken cancellationToken)
    {
        List<string> chosenTypeIds = await UserWorkflowTypeVisibilityResolver.ResolveChosenTypeIdsAsync(
            _userDataUnitOfWork, request.UserId, cancellationToken);

        // Create specification for the query
        var specification = new WorkflowExecutionsForListingSpec(request.OrganizationId, chosenTypeIds);

        // Get base query using repository with specification
        IQueryable<WorkflowExecution> baseQuery = _userDataUnitOfWork.WorkflowExecutions.GetAll(specification);

        ODataPaginatedResponse<WorkflowExecutionSummaryDto> page = await baseQuery
            .Select(WorkflowExecutionProjections.ToSummary)
            .ToODataPageAsync(request.QueryOptions, cancellationToken);
        await StampBaselineStarStatesAsync(page.Value, request.OrganizationId, cancellationToken);

        return page;
    }

    /// <summary>
    /// Stamps each baseline row's star state in one read for the whole page; non-baseline rows,
    /// and baselines the reader does not judge, stay null.
    /// </summary>
    private async Task StampBaselineStarStatesAsync(
        List<WorkflowExecutionSummaryDto> items,
        string organizationId,
        CancellationToken cancellationToken)
    {
        List<string> baselineIds = [.. items.Where(item => item.IsBaseline).Select(item => item.Id)];

        if (baselineIds.Count == 0)
        {
            return;
        }

        IReadOnlyDictionary<string, bool> verified = await _baselines.VerifiedAsync(organizationId, baselineIds, cancellationToken);

        for (int i = 0; i < items.Count; i++)
        {
            if (verified.TryGetValue(items[i].Id, out bool isVerified))
            {
                items[i] = items[i] with { BaselineVerified = isVerified };
            }
        }
    }
}

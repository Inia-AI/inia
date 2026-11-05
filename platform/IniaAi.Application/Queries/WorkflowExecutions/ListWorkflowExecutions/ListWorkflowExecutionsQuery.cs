using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using Microsoft.AspNetCore.OData.Query;

namespace IniaAi.Application.Queries.WorkflowExecutions.ListWorkflowExecutions;

/// <summary>
/// Represents a query to retrieve a paginated OData-compliant list of workflow executions.
/// </summary>
public record ListWorkflowExecutionsQuery : IRequest<ErrorOr<ODataPaginatedResponse<WorkflowExecutionSummaryDto>>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ListWorkflowExecutionsQuery"/> class.
    /// </summary>
    /// <param name="organizationId">The organization ID to filter workflow executions by.</param>
    /// <param name="userId">The user ID making the request.</param>
    /// <param name="queryOptions">The OData query options to apply for filtering, sorting, and paging.</param>
    public ListWorkflowExecutionsQuery(string organizationId, string userId, ODataQueryOptions<WorkflowExecutionSummaryDto> queryOptions)
    {
        OrganizationId = organizationId;
        UserId = userId;
        QueryOptions = queryOptions;
    }

    /// <summary>
    /// Gets the organization ID to filter workflow executions by.
    /// </summary>
    public string OrganizationId { get; }

    /// <summary>
    /// Gets the user ID making the request.
    /// </summary>
    public string UserId { get; }

    /// <summary>
    /// Gets or sets the OData query options for the workflow executions query.
    /// </summary>
    public ODataQueryOptions<WorkflowExecutionSummaryDto> QueryOptions { get; set; }
}

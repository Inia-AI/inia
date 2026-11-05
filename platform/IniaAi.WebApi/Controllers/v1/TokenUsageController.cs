using ErrorOr;
using IniaAi.Application.Common.Capabilities;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Queries.TokenUsage.GetMyUsageSummary;
using IniaAi.Application.Queries.TokenUsage.GetOrganizationUsageDashboard;
using IniaAi.Application.Queries.TokenUsage.GetWorkflowTypeUsageSummary;
using IniaAi.Application.Queries.TokenUsage.ListTokenUsage;
using IniaAi.Common.Text;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApi.Attributes;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.TokenUsage;
using IniaAi.WebApi.Controllers.v1.Base;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;

namespace IniaAi.WebApi.Controllers.v1;

/// <summary>
/// Handles token usage analytics and reporting endpoints.
/// Provides personal usage summaries, workflow type usage details, and organization-wide dashboards.
/// </summary>
[Route("api/v{version:apiVersion}/token-usage")]
public class TokenUsageController : ApiController
{
    private readonly ISender _sender;
    private readonly ILogger<TokenUsageController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenUsageController"/> class.
    /// </summary>
    /// <param name="requestContext">The request context containing organization and user information.</param>
    /// <param name="sender">Sends each application query to its handler.</param>
    /// <param name="logger">Logger instance.</param>
    public TokenUsageController(
        IRequestContext requestContext,
        ISender sender,
        ILogger<TokenUsageController> logger)
        : base(requestContext)
    {
        _sender = sender;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves the personal token usage summary for the authenticated user.
    /// </summary>
    /// <remarks>
    /// Returns token usage aggregated by:
    /// - Time periods: Today, This Week, This Month, All Time
    /// - Usage type breakdown with percentages
    /// </remarks>
    /// <returns>Personal usage summary for the current user.</returns>
    /// <response code="200">Usage summary retrieved successfully.</response>
    /// <response code="401">Unauthorized.</response>
    /// <response code="500">Unexpected server error.</response>
    [HttpGet("me")]
    [RequiresCapability(OrganizationCapabilities.USAGE_COST)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(MyUsageSummaryDto))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetMyUsageSummary()
    {
        _logger.LogInformation(
            "Retrieving personal usage summary for UserId: {UserId}, OrganizationId: {OrganizationId}",
            UserId, OrganizationId);

        var query = new GetMyUsageSummaryQuery(OrganizationId, UserId);
        ErrorOr<MyUsageSummaryDto> result = await _sender.Send(query);

        return result.Match<IActionResult>(Ok, Problem);
    }

    /// <summary>
    /// Retrieves token usage summary for a specific workflow type.
    /// </summary>
    /// <remarks>
    /// Returns detailed usage analytics for a workflow type including:
    /// - Total tokens and costs
    /// - Average tokens per execution
    /// - Breakdown by usage type
    /// - Recent execution summaries
    /// </remarks>
    /// <param name="workflowTypeId">The workflow type ID to get usage for.</param>
    /// <returns>Usage summary for the specified workflow type.</returns>
    /// <response code="200">Usage summary retrieved successfully.</response>
    /// <response code="401">Unauthorized.</response>
    /// <response code="403">The caller is neither an admin nor an operator.</response>
    /// <response code="404">Workflow type not found.</response>
    /// <response code="500">Unexpected server error.</response>
    [HttpGet("workflow-type/{workflowTypeId}")]
    [Authorize(Roles = Roles.ADMIN_AND_OPERATOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(WorkflowTypeUsageSummaryDto))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetWorkflowTypeUsageSummary(string workflowTypeId)
    {
        _logger.LogInformation(
            "Retrieving usage summary for WorkflowTypeId: {WorkflowTypeId}, OrganizationId: {OrganizationId}",
            LogSanitizer.ForLog(workflowTypeId), OrganizationId);

        var query = new GetWorkflowTypeUsageSummaryQuery(OrganizationId, UserId, workflowTypeId);
        ErrorOr<WorkflowTypeUsageSummaryDto> result = await _sender.Send(query);

        return result.Match<IActionResult>(Ok, Problem);
    }

    /// <summary>
    /// Retrieves the organization-wide token usage dashboard.
    /// </summary>
    /// <remarks>
    /// Returns comprehensive analytics for the entire organization including:
    /// - Period summaries (Today, This Week, This Month, All Time)
    /// - Breakdown by usage type, workflow type, and user
    /// - Daily trend for the last 30 days
    /// </remarks>
    /// <returns>Organization-wide usage dashboard.</returns>
    /// <response code="200">Dashboard retrieved successfully.</response>
    /// <response code="401">Unauthorized.</response>
    /// <response code="403">The caller is neither an admin nor an operator.</response>
    /// <response code="500">Unexpected server error.</response>
    [HttpGet("dashboard")]
    [Authorize(Roles = Roles.ADMIN_AND_OPERATOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(OrganizationUsageDashboardDto))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetOrganizationDashboard()
    {
        _logger.LogInformation(
            "Retrieving organization dashboard for OrganizationId: {OrganizationId}",
            OrganizationId);

        var query = new GetOrganizationUsageDashboardQuery(OrganizationId, UserId);
        ErrorOr<OrganizationUsageDashboardDto> result = await _sender.Send(query);

        return result.Match<IActionResult>(Ok, Problem);
    }

    /// <summary>
    /// Retrieves a paginated, filterable list of token usage records using OData query options.
    /// </summary>
    /// <remarks>
    /// Supports OData query capabilities such as:
    /// <list type="bullet">
    /// <item><description><c>$filter</c> - filter by properties like UsageType, Provider, etc.</description></item>
    /// <item><description><c>$orderby</c> - sort by fields like <c>createdAt</c>, <c>totalCost</c></description></item>
    /// <item><description><c>$top</c>, <c>$skip</c> - pagination</description></item>
    /// <item><description><c>$count=true</c> - include total record count</description></item>
    /// </list>
    /// </remarks>
    /// <param name="queryOptions">OData query options for filtering, sorting, and paging.</param>
    /// <param name="workflowTypeId">Optional workflow type ID to filter by.</param>
    /// <returns>An OData-compliant paginated list of token usage records.</returns>
    /// <response code="200">Token usage records retrieved successfully.</response>
    /// <response code="400">Invalid request or OData query.</response>
    /// <response code="401">Unauthorized.</response>
    /// <response code="403">The caller is neither an admin nor an operator.</response>
    /// <response code="500">Unexpected server error.</response>
    [HttpGet]
    [Authorize(Roles = Roles.ADMIN_AND_OPERATOR_ROLES)]
    [EnableCustomQuery]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ODataPaginatedResponse<TokenUsageDto>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ODataPaginatedResponse<TokenUsageDto>>> ListTokenUsage(
        ODataQueryOptions<TokenUsageDto> queryOptions,
        [FromQuery] string? workflowTypeId = null)
    {
        _logger.LogInformation(
            "Retrieving token usage list for OrganizationId: {OrganizationId}, WorkflowTypeId: {WorkflowTypeId}",
            OrganizationId, LogSanitizer.ForLog(workflowTypeId));

        var query = new ListTokenUsageQuery(OrganizationId, UserId, queryOptions, workflowTypeId);
        ErrorOr<ODataPaginatedResponse<TokenUsageDto>> result = await _sender.Send(query);

        return result.Match<ActionResult<ODataPaginatedResponse<TokenUsageDto>>>(
            pageResult => pageResult,
            errors => Problem([.. errors]));
    }
}

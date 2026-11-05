using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.OData;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.TokenUsage;

namespace IniaAi.Application.Queries.TokenUsage.ListTokenUsage;

/// <summary>
/// Handles the query to retrieve a paginated and OData-compliant list of token usage records.
/// </summary>
public class ListTokenUsageQueryHandler : IRequestHandler<ListTokenUsageQuery, ErrorOr<ODataPaginatedResponse<TokenUsageDto>>>
{
    private readonly IUserDataDbContext _dbContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListTokenUsageQueryHandler"/> class.
    /// </summary>
    /// <param name="dbContext">The database context.</param>
    public ListTokenUsageQueryHandler(
        IUserDataDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <summary>
    /// Handles the query request to return paginated token usage records.
    /// </summary>
    /// <param name="request">The request containing OData query options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A paginated OData response containing token usage DTOs.</returns>
    public async Task<ErrorOr<ODataPaginatedResponse<TokenUsageDto>>> Handle(
        ListTokenUsageQuery request,
        CancellationToken cancellationToken)
    {
        // Base query with necessary filters
        IQueryable<Domain.Entities.TokenUsage.TokenUsage> baseQuery = _dbContext.TokenUsages
            .Where(t => t.OrganizationId == request.OrganizationId)
            .AsQueryable();

        // Apply optional workflow type filter
        if (!string.IsNullOrEmpty(request.WorkflowTypeId))
        {
            baseQuery = baseQuery.Where(t => t.WorkflowTypeId == request.WorkflowTypeId);
        }

        return await baseQuery.Select(TokenUsageProjections.ToDto).ToODataPageAsync(request.QueryOptions, cancellationToken);
    }
}

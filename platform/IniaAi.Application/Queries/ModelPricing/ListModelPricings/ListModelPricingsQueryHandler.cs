using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.OData;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.ModelPricing;

namespace IniaAi.Application.Queries.ModelPricing.ListModelPricings;

/// <summary>
/// Handles the query to retrieve a paginated and OData-compliant list of model pricings.
/// </summary>
public class ListModelPricingsQueryHandler : IRequestHandler<ListModelPricingsQuery, ErrorOr<ODataPaginatedResponse<ModelPricingListItemDto>>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListModelPricingsQueryHandler"/> class.
    /// </summary>
    /// <param name="coreUnitOfWork">The core unit of work for repository access.</param>
    public ListModelPricingsQueryHandler(
        ICoreUnitOfWork coreUnitOfWork)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
    }

    /// <summary>
    /// Handles the query request to return paginated model pricings.
    /// </summary>
    /// <param name="request">The request containing OData query options.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A paginated OData response containing model pricing DTOs.</returns>
    public async Task<ErrorOr<ODataPaginatedResponse<ModelPricingListItemDto>>> Handle(ListModelPricingsQuery request, CancellationToken cancellationToken)
    {
        // Get base query - model pricing is global (not organization-specific)
        IQueryable<Domain.Entities.TokenUsage.ModelPricing> baseQuery = _coreUnitOfWork
            .Repository<Domain.Entities.TokenUsage.ModelPricing>()
            .GetAll();

        return await baseQuery.Select(ModelPricingProjections.ToListItem).ToODataPageAsync(request.QueryOptions, cancellationToken);
    }
}

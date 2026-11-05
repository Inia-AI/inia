using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.OData;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.Counterparty;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Queries.Counterparties.ListCounterparties;

/// <summary>
/// Handles the query to retrieve a paginated and OData-compliant list of <see cref="CounterpartyListItemDto"/>.
/// </summary>
public class ListCounterpartiesQueryHandler : IRequestHandler<ListCounterpartiesQuery, ErrorOr<ODataPaginatedResponse<CounterpartyListItemDto>>>
{
    private readonly IUserDataUnitOfWork _unitOfWork;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListCounterpartiesQueryHandler"/> class.
    /// </summary>
    /// <param name="unitOfWork">The unit of work for user data repository access.</param>
    public ListCounterpartiesQueryHandler(
        IUserDataUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <summary>
    /// Handles the query request to return paginated counterparties.
    /// </summary>
    /// <param name="request">The request containing OData query options.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A paginated OData response containing counterparty DTOs.</returns>
    public async Task<ErrorOr<ODataPaginatedResponse<CounterpartyListItemDto>>> Handle(ListCounterpartiesQuery request, CancellationToken cancellationToken)
    {
        // Get base query and filter by organization
        IQueryable<Counterparty> baseQuery = _unitOfWork.Repository<Counterparty>()
            .GetAll()
            .AsNoTracking()
            .Where(c => c.OrganizationId == request.OrganizationId);

        // Apply counterparty type filter at entity level (OData can't translate any() on computed DTO properties)
        if (request.CounterpartyTypeIds is { Count: > 0 })
        {
            baseQuery = baseQuery.Where(c => c.CounterpartyTypes.Any(t => request.CounterpartyTypeIds.Contains(t.Id)));
        }

        return await baseQuery.Select(CounterpartyProjections.ToListItem).ToODataPageAsync(request.QueryOptions, cancellationToken);
    }
}

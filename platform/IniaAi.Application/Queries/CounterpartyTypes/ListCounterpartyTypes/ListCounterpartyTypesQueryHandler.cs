using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.OData;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.CounterpartyTypes;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Queries.Counterparties.ListCounterpartyTypes;

/// <summary>
/// Handles the query to retrieve a paginated and OData-compliant list of <see cref="CounterpartyTypeListItemDto"/>.
/// </summary>
public class ListCounterpartyTypesQueryHandler : IRequestHandler<ListCounterpartyTypesQuery, ErrorOr<ODataPaginatedResponse<CounterpartyTypeListItemDto>>>
{
    private readonly IUserDataDbContext _context;
    private readonly BuiltInCounterpartyTypes _builtIn;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListCounterpartyTypesQueryHandler"/> class.
    /// </summary>
    /// <param name="context">The EF DbContext for user data.</param>
    /// <param name="builtIn">The counterparty types the deployment seeds and protects.</param>
    public ListCounterpartyTypesQueryHandler(
        IUserDataDbContext context,
        BuiltInCounterpartyTypes builtIn)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _builtIn = builtIn;
    }

    /// <summary>
    /// Handles the query request to return paginated counterparty types.
    /// </summary>
    /// <param name="request">The request containing OData query options.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A paginated OData response containing counterparty DTOs.</returns>
    public async Task<ErrorOr<ODataPaginatedResponse<CounterpartyTypeListItemDto>>> Handle(ListCounterpartyTypesQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> builtInNames = _builtIn.Names;

        // Base query with necessary filters
        IQueryable<CounterpartyTypeListItemDto> query = _context.CounterpartyTypes
             .AsNoTracking()
             .Select(ct => new CounterpartyTypeListItemDto
             {
                 Id = ct.Id,
                 Name = ct.Name,
                 Description = ct.Description,
                 CounterpartyCount = ct.Counterparties.Count(c => c.OrganizationId == request.OrganizationId),
                 IsBuiltIn = builtInNames.Contains(ct.Name),
             });

        return await query.ToODataPageAsync(request.QueryOptions, cancellationToken);
    }
}

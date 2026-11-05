using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Errors;
using IniaAi.Application.Mappers;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.Counterparty;

namespace IniaAi.Application.Queries.Counterparties.GetCounterparty;

/// <summary>
/// Handles retrieval of a counterparty by ID.
/// </summary>
public class GetCounterpartyQueryHandler : IRequestHandler<GetCounterpartyQuery, ErrorOr<CounterpartyDto>>
{
    private readonly IUserDataUnitOfWork _userDataDbContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetCounterpartyQueryHandler"/> class.
    /// </summary>
    /// <param name="context"></param>
    public GetCounterpartyQueryHandler(IUserDataUnitOfWork context)
    {
        _userDataDbContext = context;
    }

    /// <inheritdoc/>
    public async Task<ErrorOr<CounterpartyDto>> Handle(GetCounterpartyQuery request, CancellationToken cancellationToken)
    {
        Counterparty? entity = await _userDataDbContext.Repository<Counterparty>()
            .SingleOrDefaultAsync(c => c.Id == request.Id.ToString() && c.OrganizationId == request.OrganizationId, includeProperties: nameof(Counterparty.CounterpartyTypes), asNoTracking: true, cancellationToken: cancellationToken);

        if (entity is null)
        {
            return CounterpartyErrorFactory.NotFound(request.Id);
        }

        return CounterpartyDtoMapper.ToDto(entity);
    }
}

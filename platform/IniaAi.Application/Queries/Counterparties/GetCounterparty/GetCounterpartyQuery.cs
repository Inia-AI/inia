using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.Counterparty;

namespace IniaAi.Application.Queries.Counterparties.GetCounterparty;

/// <summary>
/// Query to retrieve a single counterparty by its ID.
/// </summary>
/// <param name="Id">The ID of the counterparty to retrieve.</param>
/// <param name="OrganizationId">The organization ID to filter by.</param>
public record GetCounterpartyQuery(string Id, string OrganizationId) : IRequest<ErrorOr<CounterpartyDto>>;

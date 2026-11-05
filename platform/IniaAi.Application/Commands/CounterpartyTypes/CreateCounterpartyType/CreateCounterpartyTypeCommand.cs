using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.CounterpartyTypes;

namespace IniaAi.Application.Commands.CounterpartyTypes.CreateCounterpartyType;

/// <summary>
/// Command to create a counterparty type. Counterparty types are shared by every organization.
/// </summary>
/// <param name="OrganizationId">The organization of the requesting user.</param>
/// <param name="UserId">The requesting user.</param>
/// <param name="Name">The name, unique ignoring case.</param>
/// <param name="Description">The description.</param>
public record CreateCounterpartyTypeCommand(
    string OrganizationId,
    string UserId,
    string Name,
    string? Description) : IRequest<ErrorOr<CounterpartyTypeListItemDto>>, ICounterpartyTypeFields;

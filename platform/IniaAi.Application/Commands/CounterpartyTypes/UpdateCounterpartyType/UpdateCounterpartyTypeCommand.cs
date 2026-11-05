using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.CounterpartyTypes;

namespace IniaAi.Application.Commands.CounterpartyTypes.UpdateCounterpartyType;

/// <summary>
/// Command to replace the name and description of a counterparty type.
/// </summary>
/// <param name="OrganizationId">The organization of the requesting user.</param>
/// <param name="UserId">The requesting user.</param>
/// <param name="Id">The counterparty type to change.</param>
/// <param name="Name">The new name, unique ignoring case. A built-in type keeps its name.</param>
/// <param name="Description">The new description.</param>
public record UpdateCounterpartyTypeCommand(
    string OrganizationId,
    string UserId,
    string Id,
    string Name,
    string? Description) : IRequest<ErrorOr<CounterpartyTypeListItemDto>>, ICounterpartyTypeFields;

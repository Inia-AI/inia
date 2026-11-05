using ErrorOr;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Commands.CounterpartyTypes.DeleteCounterpartyType;

/// <summary>
/// Command to delete a counterparty type. Its counterparties keep their other types.
/// </summary>
/// <param name="OrganizationId">The organization of the requesting user.</param>
/// <param name="UserId">The requesting user.</param>
/// <param name="Id">The counterparty type to delete. A built-in type stays.</param>
public record DeleteCounterpartyTypeCommand(string OrganizationId, string UserId, string Id) : IRequest<ErrorOr<Deleted>>;

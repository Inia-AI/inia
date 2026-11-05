using ErrorOr;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Commands.Counterparties.DeleteCounterparty;

/// <summary>
/// Command to delete a counterparty.
/// </summary>
/// <param name="Id">The ID of the counterparty to delete.</param>
/// <param name="OrganizationId">The organization ID to filter by.</param>
public record DeleteCounterpartyCommand(string Id, string OrganizationId) : IRequest<ErrorOr<Deleted>>;

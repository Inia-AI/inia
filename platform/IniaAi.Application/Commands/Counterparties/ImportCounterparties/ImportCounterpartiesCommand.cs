using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Dto;
using IniaAi.WebApi.Contracts.Responses.Counterparty;

namespace IniaAi.Application.Commands.Counterparties.ImportCounterparties;

/// <summary>
/// Lands a counterparty bundle in the organization. With <paramref name="DryRun"/> the plan is
/// computed and returned and nothing is written.
/// </summary>
public record ImportCounterpartiesCommand(
    string OrganizationId,
    string UserId,
    CounterpartyBundleDto Bundle,
    bool DryRun) : IRequest<ErrorOr<CounterpartyImportPlanDto>>;

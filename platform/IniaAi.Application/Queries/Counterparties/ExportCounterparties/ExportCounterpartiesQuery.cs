using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Dto;

namespace IniaAi.Application.Queries.Counterparties.ExportCounterparties;

/// <summary>
/// <paramref name="InstanceName"/> is what this deployment calls itself in the bundle's
/// provenance: the configured <c>PlatformSettings:InstanceName</c>, or the API host the export
/// was requested on when the key is unset.
/// </summary>
public record ExportCounterpartiesQuery(
    string OrganizationId,
    string UserId,
    string? InstanceName) : IRequest<ErrorOr<CounterpartyBundleDto>>;

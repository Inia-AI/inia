using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.TenantModelSettings;

namespace IniaAi.Application.Queries.OrganizationModelSettings.GetOrganizationModelSettings;

/// <summary>
/// Query to retrieve a single organization model settings entry by its ID.
/// </summary>
/// <param name="Id">The ID of the organization model settings to retrieve.</param>
public record GetOrganizationModelSettingsQuery(string Id) : IRequest<ErrorOr<TenantModelSettingsDto>>;

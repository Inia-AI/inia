using ErrorOr;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Commands.OrganizationModelSettings.DeleteOrganizationModelSettings;

/// <summary>
/// Command to delete an organization model settings entry.
/// </summary>
/// <param name="Id">The ID of the settings entry to delete.</param>
public record DeleteOrganizationModelSettingsCommand(string Id) : IRequest<ErrorOr<Deleted>>;

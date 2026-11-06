using IniaAi.Application.Common.Capabilities;
using IniaAi.Configuration;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Specifications.Organizations;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApp.Attributes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace IniaAi.WebApp.Services;

/// <summary>
/// The organizations the caller of a WebApp controller may act in. The MVC controllers have no tenant
/// header, so they ask here. A super admin acts in any organization not closed to staff
/// (<see cref="IOrganizationsClosedToStaff"/>), as the API's tenant check allows;
/// a signed-in user acts only in the organizations they are an active member of; the message
/// processor's API key carries no user and acts only for the organization the processor files into.
/// </summary>
public sealed class CallerOrganizations
{
    private readonly IUserDataUnitOfWork _unitOfWork;
    private readonly string _processorOrganizationId;
    private readonly IOrganizationsClosedToStaff _closed;

    public CallerOrganizations(IUserDataUnitOfWork unitOfWork, IOptions<PlatformSettings> platformSettings, IOrganizationsClosedToStaff closed)
    {
        _unitOfWork = unitOfWork;
        _processorOrganizationId = platformSettings.Value.CustomerOrgId;
        _closed = closed;
    }

    /// <summary>
    /// The organizations the caller of <paramref name="request"/> may act in, or null for every organization.
    /// </summary>
    public async Task<IReadOnlyList<string>?> ReadableAsync(HttpContext request)
    {
        if (RoleOrMessageProcessorApiKeyAuthorizeAttribute.AdmittedByApiKey(request))
        {
            return [_processorOrganizationId];
        }

        string? userId = request.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (request.User.IsInRole(Roles.SUPER_ADMIN))
        {
            if (_closed.ClosedTo(userId) is not { } closed)
            {
                return null;
            }

            return await _unitOfWork.Repository<Organization>().GetAll()
                .Where(organization => !closed.Contains(organization.Id))
                .Select(organization => organization.Id)
                .ToListAsync(request.RequestAborted);
        }

        if (string.IsNullOrEmpty(userId))
        {
            return [];
        }

        return await _unitOfWork.Repository<UserOrganization>()
            .GetAll(new UserActiveMembershipsSpec(userId))
            .Select(membership => membership.OrganizationId)
            .ToListAsync(request.RequestAborted);
    }

    public async Task<bool> CanActInAsync(HttpContext request, string organizationId)
    {
        if (request.User.IsInRole(Roles.SUPER_ADMIN) && !RoleOrMessageProcessorApiKeyAuthorizeAttribute.AdmittedByApiKey(request))
        {
            return _closed.ClosedTo(request.User.FindFirstValue(ClaimTypes.NameIdentifier)) is not { } closed
                || !await closed.ContainsAsync(organizationId, request.RequestAborted);
        }

        return await ReadableAsync(request) is not { } readable || readable.Contains(organizationId);
    }
}

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
/// header, so they ask here. A super admin acts in any organization, as the API's tenant check allows;
/// a signed-in user acts only in the organizations they are an active member of; the message
/// processor's API key carries no user and acts only for the organization the processor files into.
/// </summary>
public sealed class CallerOrganizations
{
    private readonly IUserDataUnitOfWork _unitOfWork;
    private readonly string _processorOrganizationId;

    public CallerOrganizations(IUserDataUnitOfWork unitOfWork, IOptions<PlatformSettings> platformSettings)
    {
        _unitOfWork = unitOfWork;
        _processorOrganizationId = platformSettings.Value.CustomerOrgId;
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

        if (request.User.IsInRole(Roles.SUPER_ADMIN))
        {
            return null;
        }

        string? userId = request.User.FindFirstValue(ClaimTypes.NameIdentifier);

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
        return await ReadableAsync(request) is not { } readable || readable.Contains(organizationId);
    }
}

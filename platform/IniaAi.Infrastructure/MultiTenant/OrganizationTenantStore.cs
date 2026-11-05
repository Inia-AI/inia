using Finbuckle.MultiTenant.Abstractions;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.Extensions.Logging;

namespace IniaAi.Infrastructure.MultiTenant;

/// <summary>
/// Custom tenant store that retrieves tenant information from Organization entities using Unit of Work pattern.
/// </summary>
public class OrganizationTenantStore : IMultiTenantStore<OrganizationTenantInfo>
{
    private readonly IUserDataUnitOfWork _unitOfWork;
    private readonly ILogger<OrganizationTenantStore> _logger;

    public OrganizationTenantStore(IUserDataUnitOfWork unitOfWork, ILogger<OrganizationTenantStore> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<OrganizationTenantInfo?> GetAsync(string id)
    {
        try
        {
            Organization? organization = await _unitOfWork.Organizations.GetByIdAsync(id);

            if (organization is null || !organization.IsActive)
            {
                _logger.LogDebug("Organization not found or inactive: {Id}", id);
                return null;
            }

            return new OrganizationTenantInfo(organization.Id, organization.Id)
            {
                Name = organization.Name,
                IsActive = organization.IsActive,
                CreatedAt = organization.CreatedAt,
                UpdatedAt = organization.UpdatedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tenant information for id: {Id}", id);
            return null;
        }
    }

    public async Task<OrganizationTenantInfo?> GetByIdentifierAsync(string identifier)
    {
        return await GetAsync(identifier);
    }

    public async Task<IEnumerable<OrganizationTenantInfo>> GetAllAsync()
    {
        try
        {
            // Get all active organizations (soft delete filter handles IsDeleted automatically)
            IEnumerable<Organization> organizations = await _unitOfWork.Repository<Organization>()
                .FindAsync(org => org.IsActive);

            return organizations.Select(org => new OrganizationTenantInfo(org.Id, org.Id)
            {
                Name = org.Name,
                IsActive = org.IsActive,
                CreatedAt = org.CreatedAt,
                UpdatedAt = org.UpdatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all tenant information");
            return [];
        }
    }

    public async Task<IEnumerable<OrganizationTenantInfo>> GetAllAsync(int skip, int take)
    {
        try
        {
            // Get active organizations with pagination (soft delete filter handles IsDeleted automatically)
            IEnumerable<Organization> organizations = await _unitOfWork.Repository<Organization>()
                .FindAsync(org => org.IsActive);

            return organizations
                .Skip(skip)
                .Take(take)
                .Select(org => new OrganizationTenantInfo(org.Id, org.Id)
                {
                    Name = org.Name,
                    IsActive = org.IsActive,
                    CreatedAt = org.CreatedAt,
                    UpdatedAt = org.UpdatedAt
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving paginated tenant information");
            return [];
        }
    }

    public async Task<bool> AddAsync(OrganizationTenantInfo tenantInfo)
    {
        // Not implementing add/update/remove as these should be managed through Organization entity
        // and proper business logic in the Application layer
        _logger.LogWarning("AddAsync called on OrganizationTenantStore - operations should be performed through Application layer commands");
        await Task.CompletedTask;
        return false;
    }

    public async Task<bool> RemoveAsync(string identifier)
    {
        // Not implementing add/update/remove as these should be managed through Organization entity
        // and proper business logic in the Application layer
        _logger.LogWarning("RemoveAsync called on OrganizationTenantStore - operations should be performed through Application layer commands");
        await Task.CompletedTask;
        return false;
    }

    public async Task<bool> UpdateAsync(OrganizationTenantInfo tenantInfo)
    {
        // Not implementing add/update/remove as these should be managed through Organization entity
        // and proper business logic in the Application layer
        _logger.LogWarning("UpdateAsync called on OrganizationTenantStore - operations should be performed through Application layer commands");
        await Task.CompletedTask;
        return false;
    }
}

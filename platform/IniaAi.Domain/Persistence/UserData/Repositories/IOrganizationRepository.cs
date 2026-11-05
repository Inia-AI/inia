using IniaAi.Domain.Entities.UserManagement;

namespace IniaAi.Domain.Persistence.UserData.Repositories;

public interface IOrganizationRepository : IRepository<Organization>
{
    Organization? GetById(string organizationId);
}

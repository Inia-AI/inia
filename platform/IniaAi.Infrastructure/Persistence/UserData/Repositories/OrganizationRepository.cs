using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Persistence.UserData.Repositories;
using IniaAi.Infrastructure.Persistence.Abstractions;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;

namespace IniaAi.Infrastructure.Persistence.UserData.Repositories;

public class OrganizationRepository : RepositoryBase<Organization>, IOrganizationRepository
{
    public OrganizationRepository(UserDataDbContext context)
        : base(context) { }

    public Organization? GetById(string organizationId)
    {
        return Entities.FirstOrDefault(o => o.Id == organizationId);
    }
}

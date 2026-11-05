using IniaAi.Domain.Entities.UserManagement;

namespace IniaAi.Domain.Persistence.UserData.Repositories;

public interface IUserRepository : IRepository<User>
{
    Task<bool> ExistsAsync(string userId);
    Task<IEnumerable<User>> FindByCounterpartyIdAsync(string counterpartyId);
    Task<IEnumerable<User>> FindByOrganizationIdAsync(string organizationId);
}

using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Persistence.UserData.Repositories;
using IniaAi.Infrastructure.Persistence.Abstractions;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace IniaAi.Infrastructure.Persistence.UserData.Repositories;

public class UserRepository : RepositoryBase<User>, IUserRepository
{
    public UserRepository(UserDataDbContext context)
        : base(context) { }

    public override async Task<User?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
    {
        return await Entities
            .Include(u => u.Organization)
            .Include(u => u.Counterparties)
            .FirstOrDefaultAsync(u => u.Id == id.ToString(), cancellationToken: cancellationToken);
    }

    public override async Task<IList<User>> GetAllAsync()
    {
        return await Entities
            .Include(u => u.Organization)
            .Include(u => u.Counterparties)
            .ToListAsync();
    }

    public override async Task<IEnumerable<User>> FindAsync(Expression<Func<User, bool>> predicate)
    {
        return await Entities
            .Include(u => u.Organization)
            .Include(u => u.Counterparties)
            .Where(predicate)
            .ToListAsync();
    }

    public async Task<bool> ExistsAsync(string userId)
    {
        return await Entities.AnyAsync(u => u.Id == userId);
    }

    public async Task<IEnumerable<User>> FindByCounterpartyIdAsync(string counterpartyId)
    {
        return await Entities
            .Include(u => u.Counterparties)
            .Include(u => u.Organization)
            .Where(u => u.Counterparties.Any(c => c.Id == counterpartyId))
            .ToListAsync();
    }

    public async Task<IEnumerable<User>> FindByOrganizationIdAsync(string organizationId)
    {
        return await Entities
            .Include(u => u.Counterparties)
            .Include(u => u.Organization)
            .Where(u => u.OrganizationId == organizationId)
            .ToListAsync();
    }
}

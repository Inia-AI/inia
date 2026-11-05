using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Infrastructure.Services;

public sealed class SuperAdminLookup : ISuperAdminLookup
{
    private readonly UserDataDbContext _db;

    public SuperAdminLookup(UserDataDbContext db)
    {
        _db = db;
    }

    public Task<bool> IsSuperAdminAsync(string userId, CancellationToken cancellationToken)
    {
        return _db.UserRoles
            .Where(assignment => assignment.UserId == userId)
            .Join(_db.Roles, assignment => assignment.RoleId, role => role.Id, (_, role) => role.Name)
            .AnyAsync(name => name == Roles.SUPER_ADMIN, cancellationToken);
    }
}

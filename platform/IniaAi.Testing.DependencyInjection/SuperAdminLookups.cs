using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Testing.DependencyInjection;

/// <summary>The stored role lookup over a user-data database of its own, for a test that seeds no users.</summary>
public static class SuperAdminLookups
{
    /// <summary>A lookup over an empty user-data database: nobody is a super admin.</summary>
    public static ISuperAdminLookup OverEmptyUserData()
    {
        DbContextOptions<UserDataDbContext> options = new DbContextOptionsBuilder<UserDataDbContext>()
            .UseInMemoryDatabase($"super-admins-{Guid.NewGuid()}")
            .Options;

        return new SuperAdminLookup(new UserDataDbContext(options));
    }
}

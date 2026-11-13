using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// The Identity stores both apps take: the user data store, with recovery codes kept hashed, and the
/// password work factor. One registration, so the API and the admin app cannot drift apart again.
/// </summary>
public static class PlatformIdentityStores
{
    public static IdentityBuilder AddPlatformUserStores(this IdentityBuilder identity)
    {
        _ = identity.AddEntityFrameworkStores<UserDataDbContext>().AddUserStore<RecoveryCodeHashingUserStore>();
        _ = identity.Services.Configure<PasswordHasherOptions>(PasswordHashing.Apply);
        return identity;
    }
}

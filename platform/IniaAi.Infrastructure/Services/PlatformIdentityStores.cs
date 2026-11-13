using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// The Identity stores both apps take: the user data store, with recovery codes kept hashed, the
/// password rule and work factor, and the reset token that an account without a password cannot use.
/// One registration, so the API and the admin app cannot drift apart again.
/// </summary>
public static class PlatformIdentityStores
{
    public static IdentityBuilder AddPlatformUserStores(this IdentityBuilder identity)
    {
        _ = identity.AddEntityFrameworkStores<UserDataDbContext>().AddUserStore<RecoveryCodeHashingUserStore>();
        _ = identity.AddPasswordValidator<CommonPasswordValidator>();
        _ = identity.AddTokenProvider<PasswordResetTokenProvider>(PasswordResetTokenProvider.NAME);
        _ = identity.Services.Configure<IdentityOptions>(options =>
        {
            PasswordPolicy.Apply(options.Password);
            options.Tokens.PasswordResetTokenProvider = PasswordResetTokenProvider.NAME;
        });
        _ = identity.Services.Configure<PasswordHasherOptions>(PasswordHashing.Apply);
        return identity;
    }
}

using IniaAi.Domain.Entities.UserManagement;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// The password reset token, which no account without a password can use (<see cref="PasswordReset"/>),
/// whichever page or endpoint asks Identity to reset it.
/// </summary>
public sealed class PasswordResetTokenProvider : DataProtectorTokenProvider<User>
{
    public const string NAME = "PasswordReset";

    public PasswordResetTokenProvider(
        IDataProtectionProvider dataProtectionProvider,
        IOptions<DataProtectionTokenProviderOptions> options,
        ILogger<DataProtectorTokenProvider<User>> logger)
        : base(dataProtectionProvider, options, logger)
    {
    }

    public override async Task<bool> ValidateAsync(string purpose, string token, UserManager<User> manager, User user)
    {
        return PasswordReset.IsOpenTo(user) && await base.ValidateAsync(purpose, token, manager, user);
    }
}

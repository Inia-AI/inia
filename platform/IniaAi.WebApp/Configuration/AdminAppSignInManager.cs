using System.Security.Claims;
using IniaAi.Application.Common.Capabilities;
using IniaAi.Domain.Entities.UserManagement;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace IniaAi.WebApp.Configuration;

/// <summary>
/// A user whose organization lacks the admin-app capability does not sign in to the admin app. The
/// password, second-factor and recovery-code sign-ins ask <see cref="CanSignInAsync"/> first; every
/// sign-in that issues the cookie, an outside provider's and a refresh included, ends in
/// <see cref="SignInWithClaimsAsync"/>, which refuses too.
/// </summary>
/// <remarks>
/// A staff role always signs in: an admin can make any organization a staff member's default, and
/// the check reads the default organization. Identity's recovery-code sign-in skips
/// <see cref="CanSignInAsync"/> and spends the code before it signs in, so
/// <see cref="TwoFactorRecoveryCodeSignInAsync"/> asks first, or a refused user would lose the code
/// to a sign-in reported as a success.
/// </remarks>
public sealed class AdminAppSignInManager(
    UserManager<User> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<User> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<User>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<User> confirmation,
    IOrganizationCapabilities capabilities)
    : SignInManager<User>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    private static readonly string[] _staffRoles = [Roles.SUPER_ADMIN, Roles.OPERATOR, Roles.WORKFLOW_EDITOR];

    public override async Task<bool> CanSignInAsync(User user)
    {
        return await base.CanSignInAsync(user) && await AdmitsAsync(user);
    }

    public override async Task<SignInResult> TwoFactorRecoveryCodeSignInAsync(string recoveryCode)
    {
        if (await GetTwoFactorAuthenticationUserAsync() is User user && !await CanSignInAsync(user))
        {
            return SignInResult.NotAllowed;
        }

        return await base.TwoFactorRecoveryCodeSignInAsync(recoveryCode);
    }

    public override async Task SignInWithClaimsAsync(User user, AuthenticationProperties? authenticationProperties, IEnumerable<Claim> additionalClaims)
    {
        if (await AdmitsAsync(user))
        {
            await base.SignInWithClaimsAsync(user, authenticationProperties, additionalClaims);
        }
    }

    /// <summary>Whether the admin app admits the user: a staff role, or an organization that includes the admin app.</summary>
    public async Task<bool> AdmitsAsync(User user)
    {
        foreach (string role in _staffRoles)
        {
            if (await UserManager.IsInRoleAsync(user, role))
            {
                return true;
            }
        }

        IReadOnlySet<string> granted = await capabilities.GetAsync(user.OrganizationId, CancellationToken.None);
        if (granted.Contains(OrganizationCapabilities.ADMIN_APP))
        {
            return true;
        }

        Logger.LogInformation("User {UserId} was refused the admin app: their organization does not include it", user.Id);
        return false;
    }
}

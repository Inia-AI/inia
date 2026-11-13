using IniaAi.Application.Auditing;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Entities.UserManagement;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace IniaAi.Infrastructure.Services;

/// <inheritdoc cref="IExternalLoginUserResolver"/>
public class ExternalLoginUserResolver : IExternalLoginUserResolver
{
    private readonly UserManager<User> _userManager;
    private readonly ILogger<ExternalLoginUserResolver> _logger;
    private readonly IAuthenticationAuditLog _audit;
    private readonly IExternalSignUp _signUp;

    public ExternalLoginUserResolver(
        UserManager<User> userManager,
        ILogger<ExternalLoginUserResolver> logger,
        IAuthenticationAuditLog audit,
        IExternalSignUp? signUp = null)
    {
        _userManager = userManager;
        _logger = logger;
        _audit = audit;
        _signUp = signUp ?? new NoExternalSignUp();
    }

    public async Task<ExternalLoginUserResolution> ResolveAsync(string provider, string? email, bool emailVerified, IReadOnlyCollection<string> allowedDomains, string? clientAddress = null)
    {
        ExternalLoginUserResolution resolution = await ResolveUserAsync(provider, email, emailVerified, allowedDomains, clientAddress);

        AuthenticationAuditKind kind = resolution switch
        {
            { Succeeded: true } => AuthenticationAuditKind.SignInSucceeded,
            { Failure: ExternalLoginFailure.LockedOut } => AuthenticationAuditKind.SignInLockedOut,
            _ => AuthenticationAuditKind.SignInRefused,
        };
        await _audit.RecordAsync(new AuthenticationAuditEvent(kind, resolution.User?.Id, provider.ToLowerInvariant(), clientAddress));

        return resolution;
    }

    private async Task<ExternalLoginUserResolution> ResolveUserAsync(string provider, string? email, bool emailVerified, IReadOnlyCollection<string> allowedDomains, string? clientAddress)
    {
        // Emails and domains are PII — log only presence/length, never the value.
        if (string.IsNullOrEmpty(email) || !email.Contains('@'))
        {
            _logger.LogWarning("{Provider} login failed: No valid email claim (present={EmailPresent})", provider, !string.IsNullOrEmpty(email));
            return ExternalLoginUserResolution.Failed(ExternalLoginFailure.MissingEmail);
        }

        // An address the provider does not vouch for could belong to anyone who typed it there.
        if (!emailVerified)
        {
            _logger.LogWarning("{Provider} login rejected: The provider does not vouch for the email", provider);
            return ExternalLoginUserResolution.Failed(ExternalLoginFailure.EmailNotVerified);
        }

        if (allowedDomains.Count > 0)
        {
            string domain = email.Split('@')[^1];
            if (string.IsNullOrEmpty(domain) || !allowedDomains.Contains(domain, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogWarning("{Provider} login rejected: Email domain not in allowlist (domainLength={DomainLength})", provider, domain.Length);
                return ExternalLoginUserResolution.Failed(ExternalLoginFailure.DomainNotAllowed, domain);
            }
        }

        User? user = await _userManager.FindByEmailAsync(email);
        if (user is null)
        {
            ExternalSignUpResult signUp = await _signUp.SignUpAsync(provider, email, clientAddress);
            if (signUp.Error is not null)
            {
                _logger.LogWarning("{Provider} login refused: The deployment refused to sign the address up ({Error})", provider, signUp.Error);
                return ExternalLoginUserResolution.RefusedSignUp(signUp);
            }

            user = signUp.User;
        }

        if (user is null)
        {
            _logger.LogWarning("{Provider} login failed: User not found in system", provider);
            return ExternalLoginUserResolution.Failed(ExternalLoginFailure.UserNotFound);
        }

        if (!AccountAddresses.IsSame(user.Email, email))
        {
            _logger.LogWarning("{Provider} login failed: Email only resembles the address of an account", provider);
            return ExternalLoginUserResolution.Failed(ExternalLoginFailure.UserNotFound);
        }

        if (user.IsSystem)
        {
            _logger.LogWarning("{Provider} login failed: System user attempted interactive login", provider);
            return ExternalLoginUserResolution.Failed(ExternalLoginFailure.SystemUser);
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            _logger.LogWarning("{Provider} login failed: User {UserId} is locked out", provider, user.Id);
            return ExternalLoginUserResolution.Failed(ExternalLoginFailure.LockedOut);
        }

        return ExternalLoginUserResolution.Success(user);
    }

    public async Task LinkLoginIfNeededAsync(User user, string provider, string providerKey)
    {
        IList<UserLoginInfo> logins = await _userManager.GetLoginsAsync(user);
        if (logins.Any(l => l.LoginProvider == provider && l.ProviderKey == providerKey))
        {
            return;
        }

        IdentityResult loginResult = await _userManager.AddLoginAsync(user, new UserLoginInfo(provider, providerKey, provider));
        if (!loginResult.Succeeded)
        {
            _logger.LogWarning(
                "Failed to link {Provider} login for user {UserId}: {Errors}",
                provider,
                user.Id,
                string.Join(", ", loginResult.Errors.Select(e => e.Description)));
        }
    }
}

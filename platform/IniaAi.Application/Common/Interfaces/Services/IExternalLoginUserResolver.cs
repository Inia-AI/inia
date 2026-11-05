using IniaAi.Domain.Entities.UserManagement;

namespace IniaAi.Application.Common.Interfaces.Services;

/// <summary>
/// Applies the platform's external-login policy, shared by the WebApi token grants and the
/// WebApp cookie sign-in: a user is created on external login only by <see cref="IExternalSignUp"/>,
/// which refuses unless a module fills it, the email domain must
/// pass the provider's allowlist, the email must be the account's own address apart from case
/// (not a lookalike the lookup folds onto it), and system or locked-out accounts cannot authenticate.
/// </summary>
public interface IExternalLoginUserResolver
{
    /// <summary>
    /// Resolves the platform user for a validated external identity.
    /// </summary>
    /// <param name="provider">Provider name used for log context (e.g. "Google", "OIDC").</param>
    /// <param name="email">Email claim from the validated external identity.</param>
    /// <param name="emailVerified">Whether the provider vouches for the email; an unverified email matches no account.</param>
    /// <param name="allowedDomains">Allowed email domains; empty allows all.</param>
    /// <param name="clientAddress">The client's address, for the authentication audit.</param>
    /// <returns>The resolved user, or the policy failure that rejected the login. Either way the
    /// outcome goes to the authentication audit.</returns>
    Task<ExternalLoginUserResolution> ResolveAsync(string provider, string? email, bool emailVerified, IReadOnlyCollection<string> allowedDomains, string? clientAddress = null);

    /// <summary>
    /// Records the external login on the user unless an identical link already exists.
    /// Link failures are logged and swallowed — linking is not critical to sign-in.
    /// </summary>
    Task LinkLoginIfNeededAsync(User user, string provider, string providerKey);
}

/// <summary>
/// Why an external login was rejected by <see cref="IExternalLoginUserResolver"/>.
/// </summary>
public enum ExternalLoginFailure
{
    MissingEmail,
    DomainNotAllowed,
    UserNotFound,
    SystemUser,
    LockedOut,
    EmailNotVerified,
    SignUpRefused,
}

/// <summary>
/// Result of resolving an external identity to a platform user. On a
/// <see cref="ExternalLoginFailure.DomainNotAllowed"/> failure, <paramref name="RejectedDomain"/>
/// carries the parsed domain so callers echo exactly what the allowlist rejected. On a
/// <see cref="ExternalLoginFailure.SignUpRefused"/> failure, <paramref name="SignUpRefusal"/> is the
/// sign-up's own refusal, which the token endpoint returns as it is.
/// </summary>
public record ExternalLoginUserResolution(User? User, ExternalLoginFailure? Failure, string? RejectedDomain = null, ExternalSignUpResult? SignUpRefusal = null)
{
    public bool Succeeded => User is not null;

    public static ExternalLoginUserResolution Success(User user)
    {
        return new(user, null);
    }

    public static ExternalLoginUserResolution Failed(ExternalLoginFailure failure, string? rejectedDomain = null)
    {
        return new(null, failure, rejectedDomain);
    }

    public static ExternalLoginUserResolution RefusedSignUp(ExternalSignUpResult refusal)
    {
        return new(null, ExternalLoginFailure.SignUpRefused, SignUpRefusal: refusal);
    }
}

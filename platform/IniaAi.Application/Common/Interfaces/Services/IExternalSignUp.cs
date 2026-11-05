using IniaAi.Domain.Entities.UserManagement;

namespace IniaAi.Application.Common.Interfaces.Services;

/// <summary>
/// Creates the account for an external sign-in whose address has none, where the deployment allows
/// sign-up. <see cref="IExternalLoginUserResolver"/> asks only after the provider vouched for the
/// address and the domain allowlist passed. The default signs nobody up, so an unknown address
/// stays unknown.
/// </summary>
public interface IExternalSignUp
{
    Task<ExternalSignUpResult> SignUpAsync(string provider, string email, CancellationToken cancellationToken = default);
}

/// <summary>
/// The account a sign-up created, or the OAuth error that refused it, which the token endpoint
/// returns as it is. <see cref="None"/> is a deployment that signs nobody up: the address stays
/// unknown.
/// </summary>
public sealed record ExternalSignUpResult
{
    private ExternalSignUpResult(User? user, string? error, string? errorDescription)
    {
        User = user;
        Error = error;
        ErrorDescription = errorDescription;
    }

    public static readonly ExternalSignUpResult None = new(null, null, null);

    public User? User { get; }

    public string? Error { get; }

    public string? ErrorDescription { get; }

    public static ExternalSignUpResult Created(User user)
    {
        return new(user, null, null);
    }

    public static ExternalSignUpResult Refused(string error, string errorDescription)
    {
        return new(null, error, errorDescription);
    }
}

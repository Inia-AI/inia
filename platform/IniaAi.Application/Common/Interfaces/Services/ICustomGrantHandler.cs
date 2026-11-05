using IniaAi.Domain.Entities.UserManagement;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Application.Common.Interfaces.Services;

/// <summary>
/// Serves one grant type at the token endpoint beyond the platform's own. The handler decides who
/// signs in, or refuses; the token endpoint checks the account and issues the tokens, so every grant
/// gets the same claims and session rules. The handler records its own outcome in the
/// authentication audit, and the endpoint records an account it refuses after the handler accepted
/// it. A module registers one with <see cref="CustomGrants.AddCustomGrant{THandler}"/>; a grant type
/// with no handler answers <c>unsupported_grant_type</c>.
/// </summary>
public interface ICustomGrantHandler
{
    Task<CustomGrantResult> HandleAsync(CustomGrantRequest request, CancellationToken cancellationToken);
}

/// <param name="Parameters">Every parameter of the token request, by name.</param>
/// <param name="ClientAddress">The client's address, for rate limits and the authentication audit.</param>
public sealed record CustomGrantRequest(
    string GrantType,
    IReadOnlyDictionary<string, string?> Parameters,
    string? ClientAddress);

/// <summary>The user the grant signs in, or the OAuth error that refuses it.</summary>
public sealed record CustomGrantResult
{
    private CustomGrantResult(User? user, string? error, string? errorDescription)
    {
        User = user;
        Error = error;
        ErrorDescription = errorDescription;
    }

    public User? User { get; }

    public string? Error { get; }

    public string? ErrorDescription { get; }

    public static CustomGrantResult SignIn(User user)
    {
        return new(user, null, null);
    }

    public static CustomGrantResult Refuse(string error, string errorDescription)
    {
        return new(null, error, errorDescription);
    }
}

/// <summary>A grant type a module registered, read when the token server is configured.</summary>
public sealed record CustomGrantType(string Name);

public static class CustomGrants
{
    /// <summary>
    /// Registers <typeparamref name="THandler"/> for <paramref name="grantType"/>. The handler is built
    /// by a factory, so a host that composes the module without every service the handler needs, such
    /// as a test host of another endpoint, still passes the container's validation at build.
    /// </summary>
    public static IServiceCollection AddCustomGrant<THandler>(this IServiceCollection services, string grantType)
        where THandler : class, ICustomGrantHandler
    {
        _ = services.AddKeyedScoped<ICustomGrantHandler>(grantType, (provider, _) => ActivatorUtilities.CreateInstance<THandler>(provider));
        _ = services.AddSingleton(new CustomGrantType(grantType));

        return services;
    }
}

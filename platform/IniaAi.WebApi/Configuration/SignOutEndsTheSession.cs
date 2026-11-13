using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace IniaAi.WebApi.Configuration;

/// <summary>
/// Signing out posts one token of the session to the revocation endpoint. OpenIddict revokes only that
/// token, so this ends the rest of its session too: the authorization the token belongs to and every
/// access and refresh token issued under it, including those a refresh rotated in. The user's other
/// sessions keep working.
/// </summary>
internal sealed class SignOutEndsTheSession(
    IOpenIddictAuthorizationManager authorizations,
    IOpenIddictTokenManager tokens,
    ILogger<SignOutEndsTheSession> logger) : IOpenIddictServerHandler<HandleRevocationRequestContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; }
        = OpenIddictServerHandlerDescriptor.CreateBuilder<HandleRevocationRequestContext>()
            .UseScopedHandler<SignOutEndsTheSession>()
            .SetOrder(OpenIddictServerHandlers.Revocation.RevokeToken.Descriptor.Order + 1_000)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public async ValueTask HandleAsync(HandleRevocationRequestContext context)
    {
        if (context.GenericTokenPrincipal is not { } principal || principal.GetAuthorizationId() is not { Length: > 0 } authorizationId)
        {
            return;
        }

        if (await authorizations.FindByIdAsync(authorizationId, context.CancellationToken) is object authorization)
        {
            _ = await authorizations.TryRevokeAsync(authorization, context.CancellationToken);
        }

        long revoked = await tokens.RevokeByAuthorizationIdAsync(authorizationId, context.CancellationToken);
        logger.LogInformation(
            "Signed out: revoked authorization {AuthorizationId} and {Count} tokens of user {UserId}",
            authorizationId,
            revoked,
            principal.GetClaim(OpenIddictConstants.Claims.Subject));
    }
}

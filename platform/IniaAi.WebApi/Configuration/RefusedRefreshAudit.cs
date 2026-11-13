using IniaAi.Application.Auditing;
using Microsoft.AspNetCore;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace IniaAi.WebApi.Configuration;

/// <summary>
/// Records every refused refresh in the authentication audit. OpenIddict refuses an expired, revoked
/// or reused refresh token itself, logging at Information, below the level a deployment keeps for it.
/// The refresh token's user is noted while OpenIddict reads the token, so the record names them even
/// when a later check refuses the token.
/// </summary>
internal static class RefusedRefreshAudit
{
    private const string SUBJECT_PROPERTY = "IniaAi.RefusedRefreshAudit.Subject";

    public static IEnumerable<OpenIddictServerHandlerDescriptor> Descriptors { get; } =
    [
        OpenIddictServerHandlerDescriptor.CreateBuilder<ValidateTokenContext>()
            .UseSingletonHandler<NoteTheUser>()
            .SetOrder(OpenIddictServerHandlers.Protection.ValidatePrincipal.Descriptor.Order + 500)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build(),
        OpenIddictServerHandlerDescriptor.CreateBuilder<ApplyTokenResponseContext>()
            .UseScopedHandler<RecordTheRefusal>()
            .SetOrder(int.MinValue + 100_000)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build(),
    ];

    private sealed class NoteTheUser : IOpenIddictServerHandler<ValidateTokenContext>
    {
        public ValueTask HandleAsync(ValidateTokenContext context)
        {
            if (context.Transaction.Request?.IsRefreshTokenGrantType() == true
                && context.Principal?.GetTokenType() == TokenTypeIdentifiers.RefreshToken)
            {
                context.Transaction.Properties[SUBJECT_PROPERTY] = context.Principal.GetClaim(Claims.Subject);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordTheRefusal(IAuthenticationAuditLog auditLog) : IOpenIddictServerHandler<ApplyTokenResponseContext>
    {
        public async ValueTask HandleAsync(ApplyTokenResponseContext context)
        {
            if (context.Request?.IsRefreshTokenGrantType() != true || string.IsNullOrEmpty(context.Response.Error))
            {
                return;
            }

            await auditLog.RecordAsync(
                new AuthenticationAuditEvent(
                    AuthenticationAuditKind.SignInRefused,
                    context.Transaction.GetProperty<string>(SUBJECT_PROPERTY),
                    GrantTypes.RefreshToken,
                    context.Transaction.GetHttpRequest()?.HttpContext.Connection.RemoteIpAddress?.ToString()),
                context.CancellationToken);
        }
    }
}

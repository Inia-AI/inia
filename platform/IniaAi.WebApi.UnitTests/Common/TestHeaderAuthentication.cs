using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace IniaAi.WebApi.UnitTests.Common;

/// <summary>
/// Signs a test request in as the user, organization and role its headers name, so a test host
/// runs the API's own [Authorize] gates without tokens.
/// </summary>
public sealed class TestHeaderAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SCHEME = "TestHeaders";
    public const string USER_HEADER = "X-Test-User";
    public const string ORGANIZATION_HEADER = "X-Test-Organization";
    public const string ROLE_HEADER = "X-Test-Role";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers[USER_HEADER].FirstOrDefault() is not { } userId)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        List<Claim> claims = [new(ClaimTypes.NameIdentifier, userId), new(ORGANIZATION_HEADER, Request.Headers[ORGANIZATION_HEADER].FirstOrDefault() ?? string.Empty)];
        if (Request.Headers[ROLE_HEADER].FirstOrDefault() is { } role)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SCHEME)), SCHEME)));
    }
}

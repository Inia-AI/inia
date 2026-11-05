using IniaAi.Application.Commands.UserManagement.SendPasswordReset;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Configuration.Security;
using IniaAi.WebApi.Controllers.v1.Base;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace IniaAi.WebApi.Controllers.v1;

/// <summary>
/// Provides information about authentication providers.
/// Google authentication is handled via the main /connect/token endpoint with grant_type=google.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/auth")]
public class ExternalAuthController : ApiController
{
    private readonly GoogleAuthSettings _googleSettings;
    private readonly OidcAuthSettings _oidcSettings;
    private readonly PasswordAuthSettings _passwordSettings;
    private readonly IOutgoingMail _mail;
    private readonly IConfiguration _configuration;
    private readonly IEnumerable<AdditionalSignInProvider> _additionalProviders;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalAuthController"/> class.
    /// </summary>
    public ExternalAuthController(
        IRequestContext requestContext,
        IOptions<GoogleAuthSettings> googleSettings,
        IOptions<OidcAuthSettings> oidcSettings,
        IOptions<PasswordAuthSettings> passwordSettings,
        IOutgoingMail mail,
        IConfiguration configuration,
        IEnumerable<AdditionalSignInProvider> additionalProviders)
        : base(requestContext)
    {
        _googleSettings = googleSettings.Value;
        _oidcSettings = oidcSettings.Value;
        _passwordSettings = passwordSettings.Value;
        _mail = mail;
        _configuration = configuration;
        _additionalProviders = additionalProviders;
    }

    /// <summary>
    /// Returns the status of auth providers and their configuration, and whether a password can be
    /// reset by mail, so the login screen offers "Forgot password" only when the mail can go.
    /// </summary>
    /// <returns>List of enabled auth providers and the password-reset flag.</returns>
    [HttpGet("providers")]
    [AllowAnonymous]
    public IActionResult GetProviders()
    {
        var providers = new List<object>();

        // Password authentication (email/password login)
        if (_passwordSettings.Enabled)
        {
            providers.Add(new
            {
                name = "password",
                displayName = "Email & Password"
            });
        }

        // Google OAuth authentication
        if (_googleSettings.Enabled && !string.IsNullOrEmpty(_googleSettings.ClientId))
        {
            providers.Add(new
            {
                name = "google",
                displayName = "Google",
                clientId = _googleSettings.ClientId // Frontend needs this for Google Sign-In SDK
            });
        }

        // OIDC authentication (Okta, Auth0, Azure AD, etc.)
        if (_oidcSettings.Enabled && !string.IsNullOrEmpty(_oidcSettings.ClientId))
        {
            providers.Add(new
            {
                name = "oidc",
                displayName = _oidcSettings.DisplayName,
                clientId = _oidcSettings.ClientId, // Frontend needs this for OIDC SDK initialization
                issuer = _oidcSettings.Issuer       // Frontend needs this for OIDC SDK initialization
            });
        }

        foreach (AdditionalSignInProvider provider in _additionalProviders.Where(provider => !provider.SendsMail || _mail.IsConfigured))
        {
            providers.Add(new
            {
                name = provider.Name,
                displayName = provider.DisplayName
            });
        }

        bool passwordReset = _passwordSettings.Enabled && SendPasswordResetEmailCommandHandler.CanSend(_mail, _configuration);

        return Ok(new { providers, passwordReset });
    }
}

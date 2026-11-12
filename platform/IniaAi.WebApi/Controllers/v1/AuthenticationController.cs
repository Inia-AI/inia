using Ardalis.Specification.EntityFrameworkCore;
using ErrorOr;
using Google.Apis.Auth;
using IniaAi.Application.Auditing;
using IniaAi.Application.Commands.Authentication.SendEmailOtp;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Common.Constants;
using IniaAi.Common.Text;
using IniaAi.Configuration.Security;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Services;
using IniaAi.WebApi.Configuration;
using IniaAi.WebApi.Contracts.Requests.TwoFactor;
using IniaAi.WebApi.Contracts.Responses.TwoFactor;
using IniaAi.WebApi.Controllers.v1.Base;
using IniaAi.WebApi.ExampleProviders.Authentication;
using IniaAi.WebApi.Models.Responses;
using IniaAi.WebApi.Presentation.Contracts;
using IniaAi.WebApi.Swagger;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using System.Globalization;
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace IniaAi.WebApi.Controllers.v1;

/// <summary>
/// Handles user authentication operations such as login and token refresh using OpenIddict flows.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}")]
public class AuthenticationController : ApiController
{
    /// <summary>The error of a sign-in refused because the account is locked.</summary>
    public const string ACCOUNT_LOCKED_OUT = "account_locked_out";

    /// <summary>What a module's grant tells a locked account.</summary>
    public const string ACCOUNT_LOCKED_MESSAGE = "This account is locked, so it can't sign in. Contact support to unlock it.";

    /// <summary>The error of a module's grant that signed in an account that never signs in, such as a system user.</summary>
    public const string ACCOUNT_NOT_ALLOWED = "account_not_allowed";

    /// <summary>What a module's grant tells an account that never signs in.</summary>
    public const string ACCOUNT_NOT_ALLOWED_MESSAGE = "This email address can't be used to sign in. Use your own email address.";
    private const string SESSION_ENDED = "Your session has ended. Sign in again.";

    private readonly ILogger<AuthenticationController> _logger;
    private readonly UserManager<User> _userManager;
    private readonly UserDataDbContext _userDataDbContext;
    private readonly IMfaTokenService _mfaTokenService;
    private readonly ISender _sender;
    private readonly TwoFactorSettings _twoFactorSettings;
    private readonly GoogleAuthSettings _googleSettings;
    private readonly OidcAuthSettings _oidcSettings;
    private readonly PasswordAuthSettings _passwordSettings;
    private readonly IOidcTokenValidator _oidcTokenValidator;
    private readonly IExternalLoginUserResolver _externalLoginResolver;
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly SignInAttempts _signInAttempts;
    private readonly IOutgoingMail _mail;
    private readonly ITokenGenerationService _tokenGeneration;
    private readonly ISessionLifetimePolicy _sessionLifetime;
    private readonly ISignInMethods _signInMethods;
    private readonly JwtSettings _jwtSettings;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthenticationController"/> class.
    /// </summary>
    /// <param name="requestContext">The request context containing organization and user information.</param>
    /// <param name="logger">Logger instance for authentication events.</param>
    /// <param name="userManager">UserManager to manage user identity and credentials.</param>
    /// <param name="userDataDbContext">User data database context.</param>
    /// <param name="mfaTokenService">Service for generating and validating MFA tokens.</param>
    /// <param name="sender">The sender for CQRS commands.</param>
    /// <param name="twoFactorSettings">Two-factor authentication settings.</param>
    /// <param name="googleSettings">Google authentication settings.</param>
    /// <param name="oidcSettings">OIDC authentication settings.</param>
    /// <param name="passwordSettings">Password authentication settings.</param>
    /// <param name="oidcTokenValidator">Service for validating OIDC ID tokens.</param>
    /// <param name="externalLoginResolver">Shared external-login policy (creation only through the sign-up seam, allowlists, account checks).</param>
    /// <param name="applicationManager">OpenIddict application manager for validating client credentials.</param>
    /// <param name="signInAttempts">Counts password and second-factor failures toward the account lockout.</param>
    /// <param name="mail">The deployment's outgoing mail; codes by email are offered only when it is set up.</param>
    /// <param name="tokenGeneration">Builds the claims of every token this controller issues.</param>
    /// <param name="sessionLifetime">How long each user's session may last, from its first sign-in.</param>
    /// <param name="signInMethods">Hears each sign-in and its method before the tokens are issued.</param>
    /// <param name="jwtSettings">The configured token lifetimes.</param>
    /// <param name="timeProvider">The clock the session limit is counted on.</param>
    public AuthenticationController(
        IRequestContext requestContext,
        ILogger<AuthenticationController> logger,
        UserManager<User> userManager,
        UserDataDbContext userDataDbContext,
        IMfaTokenService mfaTokenService,
        ISender sender,
        IOptions<TwoFactorSettings> twoFactorSettings,
        IOptions<GoogleAuthSettings> googleSettings,
        IOptions<OidcAuthSettings> oidcSettings,
        IOptions<PasswordAuthSettings> passwordSettings,
        IOidcTokenValidator oidcTokenValidator,
        IExternalLoginUserResolver externalLoginResolver,
        IOpenIddictApplicationManager applicationManager,
        SignInAttempts signInAttempts,
        IOutgoingMail mail,
        ITokenGenerationService tokenGeneration,
        ISessionLifetimePolicy sessionLifetime,
        ISignInMethods signInMethods,
        IOptions<JwtSettings> jwtSettings,
        TimeProvider timeProvider)
        : base(requestContext)
    {
        _logger = logger;
        _userManager = userManager;
        _userDataDbContext = userDataDbContext;
        _mfaTokenService = mfaTokenService;
        _sender = sender;
        _twoFactorSettings = twoFactorSettings.Value;
        _googleSettings = googleSettings.Value;
        _oidcSettings = oidcSettings.Value;
        _passwordSettings = passwordSettings.Value;
        _oidcTokenValidator = oidcTokenValidator;
        _externalLoginResolver = externalLoginResolver;
        _applicationManager = applicationManager;
        _signInAttempts = signInAttempts;
        _tokenGeneration = tokenGeneration;
        _mail = mail;
        _sessionLifetime = sessionLifetime;
        _signInMethods = signInMethods;
        _jwtSettings = jwtSettings.Value;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Handles token requests using password, client credentials or refresh token grant types.
    /// </summary>
    /// <remarks>
    /// Supports the following grant types:
    /// - <c>password</c>: with (email and password)
    /// - <c>client_credentials</c>: with <c>client_id</c> and <c>client_secret</c>.
    /// - <c>refresh_token</c>: to enable this pass offline_access as scope to get the refresh_token
    ///
    /// Clients must submit data as <c>application/x-www-form-urlencoded</c>:
    ///
    /// Required parameters for password grant:
    /// - grant_type=password
    /// - username
    /// - password
    /// - scope (offline_access api)
    ///
    /// Required parameters for client credentials grant:
    /// - grant_type=client_credentials
    /// - client_id
    /// - client_secret
    /// - scope (api)
    ///
    /// Required parameters for refresh token grant:
    /// - grant_type=refresh_token
    /// - refresh_token
    /// </remarks>
    /// <param name="tokenRequest">Used only for Swagger example population (not used in logic).</param>
    /// <returns>A result containing a signed-in principal or an error response.</returns>
    /// <response code="200">Token issued successfully.</response>
    /// <response code="400">Invalid request or unsupported grant type.</response>
    /// <response code="401">Authentication failed (invalid credentials, locked out, etc).</response>
    [HttpPost("connect/token")]
    [EnableRateLimiting(RateLimitingConfiguration.SIGN_IN_POLICY)]
    [IgnoreAntiforgeryToken]
    [Consumes("application/x-www-form-urlencoded")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TokenResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [SwaggerRequestExample(typeof(TokenRequest), typeof(PostLoginRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(TokenResponseExample))]
    public async Task<IActionResult> Exchange([FromForm] TokenRequest tokenRequest)
    {
        OpenIddictRequest? request = HttpContext.GetOpenIddictServerRequest();
        ArgumentNullException.ThrowIfNull(request);
        return request.GrantType switch
        {
            GrantTypes.Password => await HandlePasswordGrantAsync(request),
            GrantTypes.RefreshToken => await HandleRefreshTokenGrantAsync(),
            GrantTypes.ClientCredentials => await HandleClientCredentialsGrantAsync(request),
            ExternalLoginProviders.GOOGLE_GRANT_TYPE => await HandleGoogleGrantAsync(request),
            ExternalLoginProviders.OIDC_GRANT_TYPE => await HandleOidcGrantAsync(request),
            _ => await HandleCustomGrantAsync(request)
        };
    }

    /// <summary>
    /// Serves a grant type a module registered: its handler decides who signs in, and the account
    /// checks and the tokens are the same as for every other grant. OpenIddict already refuses a grant
    /// type nothing registered, so the first refusal below is a fallback.
    /// </summary>
    private async Task<IActionResult> HandleCustomGrantAsync(OpenIddictRequest request)
    {
        if (string.IsNullOrEmpty(request.GrantType)
            || HttpContext.RequestServices.GetKeyedService<ICustomGrantHandler>(request.GrantType) is not ICustomGrantHandler handler)
        {
            return BadRequest(new { error = Errors.UnsupportedGrantType });
        }

        CustomGrantResult result = await handler.HandleAsync(
            new CustomGrantRequest(
                request.GrantType,
                request.GetParameters().ToDictionary(parameter => parameter.Key, parameter => (string?)parameter.Value),
                ClientAddress),
            HttpContext.RequestAborted);

        if (result.User is not User user)
        {
            return BadRequest(new { error = result.Error, error_description = result.ErrorDescription });
        }

        if (await AccountRefusalAsync(user) is { } refusal)
        {
            await _signInAttempts.AccountRefusedAsync(user, request.GrantType, ClientAddress);
            return refusal == AccountRefusal.LockedOut
                ? Unauthorized(new { error = ACCOUNT_LOCKED_OUT, error_description = ACCOUNT_LOCKED_MESSAGE })
                : Unauthorized(new { error = ACCOUNT_NOT_ALLOWED, error_description = ACCOUNT_NOT_ALLOWED_MESSAGE });
        }

        return await GenerateTokenAsync(user, request.GrantType, request.GetScopes());
    }

    /// <summary>
    /// Processes a token request using the password grant type.
    /// Implements brute force protection by tracking failed attempts and locking accounts.
    /// </summary>
    /// <param name="request">The OpenIddict server request containing username and password.</param>
    /// <returns>An <see cref="IActionResult"/> containing a signed-in principal or a failure response.</returns>
    private async Task<IActionResult> HandlePasswordGrantAsync(OpenIddictRequest request)
    {
        // Check if password authentication is enabled (early rejection per OAuth2 standard)
        if (!_passwordSettings.Enabled)
        {
            _logger.LogWarning("Password grant rejected: Password authentication is disabled");
            return BadRequest(new { error = "unsupported_grant_type", error_description = "Password authentication is not enabled." });
        }

        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            _logger.LogWarning("Login failed: Username or password is missing.");
            return BadRequest(new { error = "invalid_request", error_description = "Username and password are required." });
        }

        User? user = await _userManager.FindByEmailAsync(request.Username);
        string? clientIp = ClientAddress;

        // A locked-out account answers like a wrong password, so a lockout does not reveal which accounts exist.
        PasswordOutcome passwordOutcome = await _signInAttempts.CheckPasswordAsync(user, request.Password, clientIp);
        if (user is null || passwordOutcome == PasswordOutcome.Refused)
        {
            return Forbid();
        }

        // Validate user can authenticate (not system user, email confirmed)
        // Note: Lockout already checked above
        IActionResult? validationError = await ValidateUserCanAuthenticateAsync(user, requireEmailConfirmation: true);
        if (validationError is not null)
        {
            await _signInAttempts.AccountRefusedAsync(user, clientIp);
            return validationError;
        }

        bool twoFactorEnabled = await _userManager.GetTwoFactorEnabledAsync(user);
        bool trustedDevice = twoFactorEnabled && await IsTrustedDeviceAsync(user.Id);
        bool secondFactorPending = twoFactorEnabled && !trustedDevice;

        await _signInAttempts.PasswordAcceptedAsync(user, secondFactorPending, clientIp);

        if (secondFactorPending)
        {
            // 2FA required - return 202 with MFA token (preserve requested scopes)
            return await ReturnMfaRequiredResponseAsync(user, request.GetScopes());
        }

        if (trustedDevice)
        {
            _logger.LogInformation("User {UserId} authenticated via trusted device", user.Id);
        }

        return await GenerateTokenAsync(user, GrantTypes.Password, request.GetScopes());
    }

    /// <summary>
    /// Verifies the MFA code and completes the authentication flow.
    /// </summary>
    /// <remarks>
    /// Clients must submit data as <c>application/x-www-form-urlencoded</c>:
    ///
    /// Required parameters:
    /// - mfa_token: The token received from the 202 response during login
    /// - code: The 6-digit verification code
    /// - method: The verification method (totp, email, or recovery)
    ///
    /// Optional parameters:
    /// - trust_device: Set to 'true' to trust this device for 30 days
    /// - device_fingerprint: Unique device identifier for trusted device tracking
    /// </remarks>
    /// <param name="request">The MFA verification request.</param>
    /// <returns>An <see cref="IActionResult"/> containing tokens or an error.</returns>
    /// <response code="200">Token issued successfully.</response>
    /// <response code="401">Invalid MFA token or code.</response>
    [HttpPost("connect/token/mfa")]
    [EnableRateLimiting(RateLimitingConfiguration.SIGN_IN_POLICY)]
    [IgnoreAntiforgeryToken]
    [AllowAnonymous]
    [Consumes("application/x-www-form-urlencoded")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TokenResponse))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> VerifyMfa([FromForm] VerifyMfaFormRequest request)
    {
        string? clientIp = ClientAddress;
        MfaTokenPayload? payload = _mfaTokenService.ValidateMfaToken(request.MfaToken, clientIp);

        if (payload is null)
        {
            _logger.LogWarning("MFA verification failed: Invalid or expired MFA token");
            return Unauthorized(new { error = "invalid_mfa_token", error_description = "Invalid or expired MFA token" });
        }

        User? user = await _userManager.FindByIdAsync(payload.UserId);
        if (user is null)
        {
            return Unauthorized(new { error = "user_not_found", error_description = "User not found" });
        }

        SecondFactorOutcome outcome = await _signInAttempts.VerifySecondFactorAsync(user, request.Method, request.Code, clientIp);

        if (outcome == SecondFactorOutcome.LockedOut)
        {
            return Unauthorized(new { error = ACCOUNT_LOCKED_OUT, error_description = "Too many failed attempts. The account is locked for a few minutes." });
        }

        if (outcome == SecondFactorOutcome.WrongCode)
        {
            return Unauthorized(new { error = "invalid_code", error_description = "Invalid verification code" });
        }

        // Trust device if requested
        if (request.TrustDevice && !string.IsNullOrEmpty(request.DeviceFingerprint))
        {
            await RegisterTrustedDeviceAsync(user, request.DeviceFingerprint, clientIp);
        }

        // Use scopes from MFA token payload (preserved from original login request)
        return await GenerateTokenAsync(user, GrantTypes.Password, payload.Scopes);
    }

    /// <summary>
    /// Sends an email OTP code for 2FA verification.
    /// </summary>
    /// <param name="request">The request containing the MFA token.</param>
    /// <returns>Success status.</returns>
    [HttpPost("connect/token/mfa/email")]
    [EnableRateLimiting(RateLimitingConfiguration.SIGN_IN_POLICY)]
    [AllowAnonymous]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SendEmailOtp([FromBody] SendEmailOtpRequest request)
    {
        var command = new SendEmailOtpCommand(
            request.MfaToken,
            ClientAddress);

        ErrorOr<Success> result = await _sender.Send(command);

        return result.Match<IActionResult>(
            _ => Ok(new { message = "Verification code sent to your email" }),
            Problem);
    }

    /// <summary>
    /// Creates a ClaimsIdentity with standard user claims (subject, name, email, roles, organizations).
    /// </summary>
    /// <param name="user">The user to create claims for.</param>
    /// <param name="additionalClaims">Optional additional claims to include.</param>
    /// <returns>A ClaimsIdentity populated with user claims.</returns>
    private async Task<ClaimsIdentity> CreateUserClaimsIdentityAsync(User user, params Claim[] additionalClaims)
    {
        ClaimsIdentity identity = await _tokenGeneration.GenerateClaimsIdentityAsync(user, HttpContext.RequestAborted);
        identity.AddClaims(additionalClaims);

        return identity;
    }

    private async Task<IActionResult> RecordIssuedAsync(IActionResult issued, User user, string method)
    {
        if (issued is Microsoft.AspNetCore.Mvc.SignInResult)
        {
            await _signInAttempts.TokenIssuedAsync(user, method, ClientAddress);
        }

        return issued;
    }

    /// <summary>
    /// Generates the OpenIddict token response of a new sign-in, once the sign-in seam has heard it.
    /// </summary>
    /// <param name="user">The authenticated user.</param>
    /// <param name="method">How the user signed in: the grant type.</param>
    /// <param name="scopes">The scopes to include in the token. If null, uses default scopes.</param>
    /// <param name="additionalClaims">Optional additional claims to include in the token.</param>
    /// <returns>A SignIn result with the generated token.</returns>
    private async Task<IActionResult> GenerateTokenAsync(User user, string method, IEnumerable<string>? scopes, params Claim[] additionalClaims)
    {
        await _signInMethods.SigningInAsync(user, method, HttpContext.RequestAborted);
        return await GenerateSessionTokenAsync(user, scopes, _timeProvider.GetUtcNow(), additionalClaims);
    }

    /// <summary>
    /// Issues the tokens of a session that started at <paramref name="signedInAt"/>, or refuses the
    /// session once it reached the user's limit. The refresh token carries the sign-in time as
    /// <c>auth_time</c>. No token outlives the limit, and the token that reaches it comes without a
    /// refresh token, so no client refreshes a session that has no time left to give.
    /// </summary>
    private async Task<IActionResult> GenerateSessionTokenAsync(
        User user,
        IEnumerable<string>? scopes,
        DateTimeOffset signedInAt,
        params Claim[] additionalClaims)
    {
        SessionLifetime lifetime = await _sessionLifetime.GetAsync(user, HttpContext.RequestAborted);
        TimeSpan accessLifetime = _jwtSettings.AccessTokenLifetime;
        TimeSpan refreshLifetime = lifetime.IdleLifetime ?? _jwtSettings.RefreshTokenLifetime;
        bool lastOfSession = false;
        if (lifetime.AbsoluteLimit is TimeSpan limit)
        {
            TimeSpan remaining = signedInAt + limit - _timeProvider.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                _logger.LogInformation("Token request refused: the session of user {UserId} reached its limit", user.Id);
                return Forbid(
                    new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = SESSION_ENDED,
                    }),
                    OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            }

            lastOfSession = remaining <= accessLifetime;
            accessLifetime = lastOfSession ? remaining : accessLifetime;
            refreshLifetime = refreshLifetime < remaining ? refreshLifetime : remaining;
        }

        ClaimsIdentity identity = await CreateUserClaimsIdentityAsync(user, additionalClaims);
        identity.AddClaim(new Claim(
            Claims.AuthenticationTime,
            signedInAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            ClaimValueTypes.Integer64));
        var principal = new ClaimsPrincipal(identity);

        // Use provided scopes or default to offline_access and api
        IEnumerable<string> effectiveScopes = scopes ?? new[] { Scopes.OfflineAccess, "api" };
        _ = principal.SetScopes(lastOfSession ? effectiveScopes.Where(scope => scope != Scopes.OfflineAccess) : effectiveScopes);
        _ = principal.SetAccessTokenLifetime(accessLifetime);
        _ = principal.SetRefreshTokenLifetime(refreshLifetime);

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Validates that a user can authenticate (not locked out, email confirmed for password flow).
    /// </summary>
    /// <param name="user">The user to validate.</param>
    /// <param name="requireEmailConfirmation">Whether to require email confirmation.</param>
    /// <returns>Null if valid, or an error IActionResult if validation fails.</returns>
    private async Task<IActionResult?> ValidateUserCanAuthenticateAsync(User user, bool requireEmailConfirmation = false)
    {
        return await AccountRefusalAsync(user, requireEmailConfirmation) is null ? null : Forbid();
    }

    /// <summary>Why the account may not sign in, logged, or null when it may.</summary>
    private async Task<AccountRefusal?> AccountRefusalAsync(User user, bool requireEmailConfirmation = false)
    {
        if (user.IsSystem)
        {
            _logger.LogWarning("Login failed: System user attempted interactive login");
            return AccountRefusal.SystemUser;
        }

        if (requireEmailConfirmation && !await _userManager.IsEmailConfirmedAsync(user))
        {
            _logger.LogWarning("Login failed: Email not confirmed for user {UserId}", user.Id);
            return AccountRefusal.EmailNotConfirmed;
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            _logger.LogWarning("Login failed: User {UserId} is locked out", user.Id);
            return AccountRefusal.LockedOut;
        }

        return null;
    }

    private enum AccountRefusal
    {
        SystemUser,
        EmailNotConfirmed,
        LockedOut,
    }

    /// <summary>
    /// Checks if the current request is from a trusted device for the given user.
    /// </summary>
    private async Task<bool> IsTrustedDeviceAsync(string userId)
    {
        string? deviceFingerprint = Request.Headers["X-Device-Fingerprint"].FirstOrDefault();
        return !string.IsNullOrEmpty(deviceFingerprint) && await _userDataDbContext.UserTrustedDevices
            .AnyAsync(d => d.UserId == userId
                && d.DeviceFingerprint == deviceFingerprint
                && d.TrustedUntil > DateTime.UtcNow);
    }

    /// <summary>
    /// Returns a 202 Accepted response indicating MFA is required.
    /// </summary>
    /// <param name="user">The user requiring MFA.</param>
    /// <param name="scopes">The requested OAuth scopes to preserve through the MFA flow.</param>
    private async Task<IActionResult> ReturnMfaRequiredResponseAsync(User user, IEnumerable<string>? scopes = null)
    {
        string? clientIp = ClientAddress;
        string mfaToken = _mfaTokenService.GenerateMfaToken(user.Id, user.Email ?? "", clientIp, scopes);
        List<string> availableMethods = await GetAvailableMfaMethodsAsync(user);

        _logger.LogInformation("2FA required for user {UserId}", user.Id);

        return StatusCode(StatusCodes.Status202Accepted, new TwoFactorRequiredResponse
        {
            MfaToken = mfaToken,
            AvailableMethods = availableMethods,
            MaskedEmail = MaskEmail(user.Email ?? ""),
            ExpiresIn = _twoFactorSettings.MfaTokenValidityMinutes * 60
        });
    }

    /// <summary>
    /// Registers a trusted device for a user after successful MFA verification.
    /// </summary>
    private async Task RegisterTrustedDeviceAsync(User user, string deviceFingerprint, string? clientIp)
    {
        var trustedDevice = new UserTrustedDevice
        {
            UserId = user.Id,
            DeviceFingerprint = deviceFingerprint,
            DeviceName = Request.Headers.UserAgent.ToString(),
            TrustedUntil = DateTime.UtcNow.AddDays(_twoFactorSettings.TrustedDeviceDays),
            IpAddress = clientIp,
            UserAgent = Request.Headers.UserAgent.ToString()
        };
        _ = _userDataDbContext.UserTrustedDevices.Add(trustedDevice);
        _ = await _userDataDbContext.SaveChangesAsync();
        _logger.LogInformation("Device trusted for user {UserId}", user.Id);
    }

    /// <summary>
    /// Gets the available MFA methods for a user.
    /// </summary>
    private async Task<List<string>> GetAvailableMfaMethodsAsync(User user)
    {
        var methods = new List<string>();

        bool hasAuthenticator = await _userManager.GetAuthenticatorKeyAsync(user) is not null;
        if (hasAuthenticator)
        {
            methods.Add(AuthenticationAuditMethods.TOTP);
        }

        if (_mail.IsConfigured && !string.IsNullOrEmpty(user.Email))
        {
            methods.Add(AuthenticationAuditMethods.EMAIL);
        }

        int recoveryCodesLeft = await _userManager.CountRecoveryCodesAsync(user);
        if (recoveryCodesLeft > 0)
        {
            methods.Add(AuthenticationAuditMethods.RECOVERY);
        }

        return methods;
    }

    /// <summary>
    /// Masks an email address for display.
    /// </summary>
    private static string MaskEmail(string email)
    {
        if (string.IsNullOrEmpty(email))
        {
            return "";
        }

        string[] parts = email.Split('@');
        if (parts.Length != 2)
        {
            return email;
        }

        string localPart = parts[0];
        string masked = localPart.Length > 2
            ? $"{localPart[0]}***{localPart[^1]}"
            : $"{localPart[0]}***";

        return $"{masked}@{parts[1]}";
    }

    /// <summary>
    /// Processes a token request using the refresh token grant type.
    /// </summary>
    /// <returns>An <see cref="IActionResult"/> containing a signed-in principal or a failure response.</returns>
    private async Task<IActionResult> HandleRefreshTokenGrantAsync()
    {
        AuthenticateResult result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        if (result?.Principal is null)
        {
            _logger.LogWarning("Refresh token flow failed: Principal not found");
            return Forbid();
        }

        string? userId = result.Principal.GetClaim(Claims.Subject);
        User? user = string.IsNullOrEmpty(userId) ? null : await _userManager.FindByIdAsync(userId);

        if (user is null)
        {
            _logger.LogWarning("Refresh token flow failed: user not found");
            return Forbid();
        }

        IActionResult? validationError = await ValidateUserCanAuthenticateAsync(user);
        if (validationError is not null)
        {
            return validationError;
        }

        // A refresh token issued before sessions were counted carries no sign-in time; its session
        // is counted from this refresh.
        DateTimeOffset signedInAt = long.TryParse(
                result.Principal.GetClaim(Claims.AuthenticationTime), NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : _timeProvider.GetUtcNow();

        // Rebuilt from the user's current roles and memberships, so a refresh never re-signs what
        // the original sign-in granted.
        return await RecordIssuedAsync(await GenerateSessionTokenAsync(user, result.Principal.GetScopes(), signedInAt), user, GrantTypes.RefreshToken);
    }

    /// <summary>
    /// Processes a token request using the client credentials grant type.
    /// </summary>
    /// <param name="request">The OpenIddict server request containing client credentials.</param>
    /// <returns>An <see cref="IActionResult"/> containing a signed-in principal or a failure response.</returns>
    private async Task<IActionResult> HandleClientCredentialsGrantAsync(OpenIddictRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            _logger.LogWarning("Client credentials flow failed: client_id missing");
            return BadRequest(new { error = "invalid_request", error_description = "Client ID is required." });
        }

        // OpenIddict checks the secret of a confidential client, but a client seeded before a secret
        // was configured is public and carries none to check. Validate here as well, so knowing a
        // client_id alone never yields a privileged service-account token.
        object? application = await _applicationManager.FindByClientIdAsync(request.ClientId);

        if (application is null)
        {
            _logger.LogWarning("Client credentials flow failed: unknown client_id {ClientId}", LogSanitizer.ForLog(request.ClientId));
            return Forbid();
        }

        if (string.IsNullOrEmpty(request.ClientSecret)
            || !await _applicationManager.ValidateClientSecretAsync(application, request.ClientSecret))
        {
            _logger.LogWarning("Client credentials flow failed: invalid client_secret for {ClientId}", LogSanitizer.ForLog(request.ClientId));
            return Forbid();
        }

        User? systemUser = await _userManager.FindByNameAsync(request.ClientId);

        if (systemUser is null || !systemUser.IsSystem)
        {
            _logger.LogWarning("Client credentials flow failed: No system user found for client_id {ClientId}", LogSanitizer.ForLog(request.ClientId));
            return Forbid();
        }

        // Include client_id claim for client credentials flow
        Claim clientIdClaim = new Claim(Claims.ClientId, request.ClientId).SetDestinations(Destinations.AccessToken);

        return await RecordIssuedAsync(
            await GenerateTokenAsync(systemUser, GrantTypes.ClientCredentials, request.GetScopes().Remove(Scopes.OfflineAccess), clientIdClaim),
            systemUser,
            GrantTypes.ClientCredentials);
    }

    /// <summary>
    /// Processes a token request using the Google grant type.
    /// Validates the Google ID token and issues OpenIddict tokens.
    /// </summary>
    /// <param name="request">The OpenIddict server request containing the Google credential.</param>
    /// <returns>An <see cref="IActionResult"/> containing a signed-in principal or a failure response.</returns>
    private async Task<IActionResult> HandleGoogleGrantAsync(OpenIddictRequest request)
    {
        if (!_googleSettings.Enabled)
        {
            return BadRequest(new { error = "google_disabled", error_description = "Google authentication is not enabled." });
        }

        string? credential = request.GetParameter("credential")?.ToString();
        if (string.IsNullOrEmpty(credential))
        {
            _logger.LogWarning("Google login failed: Credential is missing");
            return BadRequest(new { error = "invalid_request", error_description = "Google credential is required." });
        }

        // Validate the Google ID token
        (GoogleJsonWebSignature.Payload? Payload, IActionResult? Error) payloadResult = await ValidateGoogleCredentialAsync(credential);
        if (payloadResult.Error is not null)
        {
            return payloadResult.Error;
        }

        GoogleJsonWebSignature.Payload payload = payloadResult.Payload!;

        ExternalLoginUserResolution resolution = await _externalLoginResolver.ResolveAsync(
            ExternalLoginProviders.GOOGLE, payload.Email, payload.EmailVerified, _googleSettings.AllowedDomains, ClientAddress);
        if (!resolution.Succeeded)
        {
            return MapExternalLoginFailure(resolution, ExternalLoginProviders.GOOGLE, echoRejectedDomain: true);
        }

        User user = resolution.User!;
        await _externalLoginResolver.LinkLoginIfNeededAsync(user, ExternalLoginProviders.GOOGLE, payload.Subject);

        _logger.LogInformation("User {UserId} authenticated via Google", user.Id);

        // Use requested scopes from the OAuth request
        return await GenerateTokenAsync(user, ExternalLoginProviders.GOOGLE_GRANT_TYPE, request.GetScopes());
    }

    /// <summary>
    /// Processes a token request using the OIDC grant type.
    /// Validates the OIDC ID token and issues OpenIddict tokens.
    /// </summary>
    /// <param name="request">The OpenIddict server request containing the OIDC credential.</param>
    /// <returns>An <see cref="IActionResult"/> containing a signed-in principal or a failure response.</returns>
    private async Task<IActionResult> HandleOidcGrantAsync(OpenIddictRequest request)
    {
        if (!_oidcSettings.Enabled)
        {
            return BadRequest(new { error = "oidc_disabled", error_description = "OIDC authentication is not enabled." });
        }

        string? credential = request.GetParameter("credential")?.ToString();
        if (string.IsNullOrEmpty(credential))
        {
            _logger.LogWarning("OIDC login failed: Credential is missing");
            return BadRequest(new { error = "invalid_request", error_description = "OIDC credential is required." });
        }

        // Validate the OIDC ID token
        OidcTokenValidationResult tokenResult = await _oidcTokenValidator.ValidateTokenAsync(credential);
        if (!tokenResult.IsValid)
        {
            _logger.LogWarning("OIDC token validation failed: {Error}", tokenResult.Error);
            return BadRequest(new { error = "invalid_token", error_description = "Invalid OIDC credential." });
        }

        ExternalLoginUserResolution resolution = await _externalLoginResolver.ResolveAsync(
            ExternalLoginProviders.OIDC, tokenResult.Email, tokenResult.EmailVerified, _oidcSettings.AllowedDomains, ClientAddress);
        if (!resolution.Succeeded)
        {
            return MapExternalLoginFailure(resolution, ExternalLoginProviders.OIDC, echoRejectedDomain: false);
        }

        User user = resolution.User!;
        if (!string.IsNullOrEmpty(tokenResult.Subject))
        {
            await _externalLoginResolver.LinkLoginIfNeededAsync(user, ExternalLoginProviders.OIDC, tokenResult.Subject);
        }

        _logger.LogInformation("User {UserId} authenticated via OIDC", user.Id);

        // Use requested scopes from the OAuth request
        return await GenerateTokenAsync(user, ExternalLoginProviders.OIDC_GRANT_TYPE, request.GetScopes());
    }

    /// <summary>
    /// Maps a shared-policy failure to the grant's wire response. <paramref name="echoRejectedDomain"/>
    /// preserves the Google grant's legacy behavior of echoing the rejected domain in the response
    /// body; the OIDC grant never does. A refused sign-up returns the sign-up's own error and message.
    /// </summary>
    private IActionResult MapExternalLoginFailure(ExternalLoginUserResolution resolution, string provider, bool echoRejectedDomain)
    {
        return resolution.Failure switch
        {
            ExternalLoginFailure.SignUpRefused => BadRequest(new
            {
                error = resolution.SignUpRefusal?.Error,
                error_description = resolution.SignUpRefusal?.ErrorDescription
            }),
            ExternalLoginFailure.MissingEmail => BadRequest(new
            {
                error = "missing_email",
                error_description = $"{provider} token does not contain a valid email claim."
            }),
            ExternalLoginFailure.EmailNotVerified => BadRequest(new
            {
                error = "email_not_verified",
                error_description = $"{provider} does not confirm that this email address belongs to you."
            }),
            ExternalLoginFailure.DomainNotAllowed => BadRequest(new
            {
                error = "domain_not_allowed",
                error_description = echoRejectedDomain && resolution.RejectedDomain is { } domain
                    ? $"Email domain '{domain}' is not allowed."
                    : "Email domain is not allowed."
            }),
            ExternalLoginFailure.UserNotFound => BadRequest(new
            {
                error = "user_not_found",
                error_description = "Your account is not registered. Please contact your administrator."
            }),
            _ => Forbid(),
        };
    }

    /// <summary>
    /// Validates a Google credential and returns the payload.
    /// </summary>
    private async Task<(GoogleJsonWebSignature.Payload? Payload, IActionResult? Error)> ValidateGoogleCredentialAsync(string credential)
    {
        GoogleJsonWebSignature.Payload payload;
        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { _googleSettings.ClientId }
            };

            payload = await GoogleJsonWebSignature.ValidateAsync(credential, settings);
        }
        catch (InvalidJwtException ex)
        {
            _logger.LogWarning("Google token validation failed: {Message}", ex.Message);
            return (null, BadRequest(new { error = "invalid_token", error_description = "Invalid Google credential." }));
        }

        return (payload, null);
    }
}

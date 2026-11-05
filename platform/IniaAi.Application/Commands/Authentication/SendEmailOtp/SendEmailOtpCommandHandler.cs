using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Configuration.Security;
using IniaAi.Domain.Entities.UserManagement;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IniaAi.Application.Commands.Authentication.SendEmailOtp;

/// <summary>
/// Handles the command to send an email OTP code. The code goes out through the deployment's
/// mail, and the request is refused when no mail provider is set up.
/// </summary>
public class SendEmailOtpCommandHandler : IRequestHandler<SendEmailOtpCommand, ErrorOr<Success>>
{
    public const string UNAVAILABLE = "EmailOtp.Unavailable";

    private const string EmailOtpCooldownCacheKeyPrefix = "EmailOtpCooldown_";

    private readonly IMfaTokenService _mfaTokenService;
    private readonly UserManager<User> _userManager;
    private readonly IMemoryCache _cache;
    private readonly TwoFactorSettings _settings;
    private readonly IOutgoingMail _mail;
    private readonly ILogger<SendEmailOtpCommandHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SendEmailOtpCommandHandler"/> class.
    /// </summary>
    public SendEmailOtpCommandHandler(
        IMfaTokenService mfaTokenService,
        UserManager<User> userManager,
        IMemoryCache cache,
        IOptions<TwoFactorSettings> settings,
        IOutgoingMail mail,
        ILogger<SendEmailOtpCommandHandler> logger)
    {
        _mfaTokenService = mfaTokenService ?? throw new ArgumentNullException(nameof(mfaTokenService));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _mail = mail ?? throw new ArgumentNullException(nameof(mail));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<ErrorOr<Success>> Handle(SendEmailOtpCommand request, CancellationToken cancellationToken)
    {
        MfaTokenPayload? payload = _mfaTokenService.ValidateMfaToken(request.MfaToken, request.ClientIpAddress);

        if (payload is null)
        {
            return Error.Unauthorized("MfaToken.Invalid", "Invalid or expired MFA token");
        }

        User? user = await _userManager.FindByIdAsync(payload.UserId);
        if (user is null || string.IsNullOrEmpty(user.Email))
        {
            return Error.Unauthorized("User.NotFound", "User not found");
        }

        if (!_mail.IsConfigured)
        {
            return Error.Validation(UNAVAILABLE, "Codes by email are off on this deployment. Use your authenticator app or a recovery code.");
        }

        // Check rate limiting
        string cooldownCacheKey = $"{EmailOtpCooldownCacheKeyPrefix}{user.Id}";
        if (_cache.TryGetValue(cooldownCacheKey, out DateTime lastSentTime))
        {
            TimeSpan timeSinceLastRequest = DateTime.UtcNow - lastSentTime;
            int cooldownSeconds = _settings.EmailOtp.CooldownSeconds;

            if (timeSinceLastRequest.TotalSeconds < cooldownSeconds)
            {
                int remainingSeconds = (int)(cooldownSeconds - timeSinceLastRequest.TotalSeconds);
                _logger.LogWarning("Email OTP rate limit hit for user {UserId}. {RemainingSeconds}s remaining", user.Id, remainingSeconds);
                return Error.Conflict("EmailOtp.RateLimited", $"Please wait {remainingSeconds} seconds before requesting another code");
            }
        }

        string code = await _userManager.GenerateTwoFactorTokenAsync(user, TokenOptions.DefaultEmailProvider);

        await _mail.SendAsync(
            user.Email,
            "Your sign-in code",
            $"<p>Your sign-in code is <strong>{code}</strong>. It works for a few minutes. If you did not try to sign in, change your password.</p>");

        _logger.LogInformation("Email OTP sent to user {UserId}", user.Id);

        // Update rate limiting cache
        _ = _cache.Set(cooldownCacheKey, DateTime.UtcNow, TimeSpan.FromSeconds(_settings.EmailOtp.CooldownSeconds * 2));

        return Result.Success;
    }
}

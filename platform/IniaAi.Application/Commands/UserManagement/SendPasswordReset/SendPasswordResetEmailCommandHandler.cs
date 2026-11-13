using ErrorOr;
using IniaAi.Application.Common;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Configuration.Security;
using IniaAi.Domain.Entities.UserManagement;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;

namespace IniaAi.Application.Commands.UserManagement.SendPasswordReset;

/// <summary>
/// Handles the <see cref="SendPasswordResetEmailCommand"/> by generating a reset token and mailing,
/// to the address stored on the account, a link to the analyst app's reset page under
/// <c>FrontendBaseUrl</c>. With no mail provider or no front-end address it sends nothing, so a
/// token never goes to a built-in address; while password sign-in is off, or for an account with no
/// password (<see cref="PasswordReset"/>), it sends nothing either.
/// </summary>
public class SendPasswordResetEmailCommandHandler : IRequestHandler<SendPasswordResetEmailCommand, ErrorOr<Success>>
{
    public const string FRONTEND_BASE_URL_KEY = AppAddresses.FRONTEND_BASE_URL_KEY;
    public const string RESET_PATH = "/reset-password";

    private readonly UserManager<User> _userManager;
    private readonly IOutgoingMail _mail;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SendPasswordResetEmailCommandHandler> _logger;
    private readonly PasswordAuthSettings _passwordSettings;

    /// <summary>
    /// Initializes a new instance of the <see cref="SendPasswordResetEmailCommandHandler"/> class.
    /// </summary>
    /// <param name="userManager">The user manager for looking up users and generating tokens.</param>
    /// <param name="mail">The deployment's outgoing mail.</param>
    /// <param name="configuration">Application configuration (the analyst app's address).</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="passwordSettings">Whether password sign-in is on; while it is off no reset mail goes.</param>
    public SendPasswordResetEmailCommandHandler(
        UserManager<User> userManager,
        IOutgoingMail mail,
        IConfiguration configuration,
        ILogger<SendPasswordResetEmailCommandHandler> logger,
        IOptions<PasswordAuthSettings> passwordSettings)
    {
        _userManager = userManager;
        _mail = mail;
        _configuration = configuration;
        _logger = logger;
        _passwordSettings = passwordSettings.Value;
    }

    /// <summary>
    /// Whether a reset mail can go: it needs a mail provider and the analyst app's address.
    /// </summary>
    public static bool CanSend(IOutgoingMail mail, IConfiguration configuration)
    {
        return mail.IsConfigured && !string.IsNullOrWhiteSpace(configuration[FRONTEND_BASE_URL_KEY]);
    }

    /// <inheritdoc />
    public async Task<ErrorOr<Success>> Handle(SendPasswordResetEmailCommand request, CancellationToken cancellationToken)
    {
        if (!_passwordSettings.Enabled || !CanSend(_mail, _configuration))
        {
            return Result.Success;
        }

        User? user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            _logger.LogWarning("Password reset requested for an address with no account");
            return Result.Success; // Don't reveal existence
        }

        if (!PasswordReset.IsOpenTo(user))
        {
            _logger.LogWarning("Password reset requested for an account with no password");
            return Result.Success;
        }

        try
        {
            // Never request.Email: the lookup also matches lookalikes of the stored address (CVE-2026-70175).
            string storedEmail = user.Email!;
            string token = await _userManager.GeneratePasswordResetTokenAsync(user);
            string link = $"{_configuration[FRONTEND_BASE_URL_KEY]!.TrimEnd('/')}{RESET_PATH}?email={WebUtility.UrlEncode(storedEmail)}&token={WebUtility.UrlEncode(token)}";

            await _mail.SendAsync(
                storedEmail,
                "Reset your password",
                $"<p>Click <a href='{WebUtility.HtmlEncode(link)}'>here</a> to reset your password.</p>");

            _logger.LogInformation("Password reset mail sent");
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A failure answers like an unknown email, so it cannot reveal that the account exists.
            _logger.LogError(exception, "Password reset mail could not be sent");
        }

        return Result.Success;
    }
}

using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Entities.UserManagement;
using Microsoft.AspNetCore.Identity;

namespace IniaAi.WebApp.Components.Account;

/// <summary>
/// Sends the admin app's account mail (confirmation and password reset) through the deployment's
/// outgoing mail. With no mail provider it sends nothing and logs an error; the forgot-password
/// page says so before anyone asks for a mail.
/// </summary>
public sealed class IdentityMailSender(IOutgoingMail mail, ILogger<IdentityMailSender> logger) : IEmailSender<User>
{
    public Task SendConfirmationLinkAsync(User user, string email, string confirmationLink)
    {
        return SendAsync(email, "Confirm your email", $"Please confirm your account by <a href='{confirmationLink}'>clicking here</a>.");
    }

    public Task SendPasswordResetLinkAsync(User user, string email, string resetLink)
    {
        return SendAsync(email, "Reset your password", $"Please reset your password by <a href='{resetLink}'>clicking here</a>.");
    }

    public Task SendPasswordResetCodeAsync(User user, string email, string resetCode)
    {
        return SendAsync(email, "Reset your password", $"Please reset your password using the following code: {resetCode}");
    }

    private Task SendAsync(string email, string subject, string htmlBody)
    {
        if (!mail.IsConfigured)
        {
            logger.LogError("Account mail '{Subject}' not sent: no mail provider is configured", subject);
            return Task.CompletedTask;
        }

        return mail.SendAsync(email, subject, htmlBody);
    }
}

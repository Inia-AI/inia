using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Configuration.Email;
using IniaAi.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IniaAi.Infrastructure.Email;

/// <summary>
/// Sends through the provider that <c>EmailSettings:Provider</c> names, once that provider has its
/// settings: SMTP needs a host and a sender, SendGrid an API key and a sender, SES a sender.
/// </summary>
public sealed class OutgoingMail(IEmailSender? sender) : IOutgoingMail
{
    public const string PROVIDER_KEY = "EmailSettings:Provider";

    public bool IsConfigured => sender is not null;

    public Task SendAsync(OutgoingMailMessage message)
    {
        if (sender is null)
        {
            throw new InvalidOperationException(
                $"No mail provider is configured. Set {PROVIDER_KEY} to Smtp, SendGrid or Ses, with that provider's settings.");
        }

        return sender.SendEmailAsync(message.WithoutLineBreaksInHeaders());
    }

    /// <summary>
    /// The sender for the provider <paramref name="providerName"/> names, or none when that provider
    /// lacks its settings.
    /// </summary>
    public static IEmailSender? ConfiguredSender(string? providerName, IServiceProvider services)
    {
        if (!Enum.TryParse(providerName, ignoreCase: true, out EmailProvider provider))
        {
            return null;
        }

        SmtpSettings smtp = services.GetRequiredService<IOptions<SmtpSettings>>().Value;
        SendGridSettings sendGrid = services.GetRequiredService<IOptions<SendGridSettings>>().Value;
        SesSettings ses = services.GetRequiredService<IOptions<SesSettings>>().Value;

        return provider switch
        {
            EmailProvider.Smtp when !string.IsNullOrWhiteSpace(smtp.Host) && !string.IsNullOrWhiteSpace(smtp.FromEmail)
                => ActivatorUtilities.CreateInstance<SmtpEmailSender>(services),
            EmailProvider.SendGrid when !string.IsNullOrWhiteSpace(sendGrid.ApiKey) && !string.IsNullOrWhiteSpace(sendGrid.FromEmail)
                => ActivatorUtilities.CreateInstance<SendGridEmailSender>(services),
            EmailProvider.Ses when !string.IsNullOrWhiteSpace(ses.FromEmail)
                => ActivatorUtilities.CreateInstance<SesEmailSender>(services),
            _ => null,
        };
    }
}

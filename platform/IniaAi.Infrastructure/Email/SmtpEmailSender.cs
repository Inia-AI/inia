using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Configuration.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;

namespace IniaAi.Infrastructure.Email;

/// <summary>
/// Provides functionality for sending emails using an SMTP server.
/// </summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly SmtpSettings _settings;
    private readonly ILogger<SmtpEmailSender> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SmtpEmailSender"/> class.
    /// </summary>
    /// <param name="options">The SMTP settings.</param>
    /// <param name="logger">Logger instance for logging operations.</param>
    public SmtpEmailSender(IOptions<SmtpSettings> options, ILogger<SmtpEmailSender> logger)
    {
        _settings = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SendEmailAsync(OutgoingMailMessage outgoing)
    {
        using MailMessage message = Compose(outgoing, _settings);

        using var client = new SmtpClient(_settings.Host, _settings.Port)
        {
            EnableSsl = _settings.EnableSsl,
            Credentials = new NetworkCredential(_settings.Username, _settings.Password)
        };

        await client.SendMailAsync(message);
        _logger.LogInformation("SMTP email sent");
    }

    /// <summary>The SMTP message for <paramref name="outgoing"/>, from the configured sender.</summary>
    internal static MailMessage Compose(OutgoingMailMessage outgoing, SmtpSettings settings)
    {
        var message = new MailMessage
        {
            From = new MailAddress(settings.FromEmail, settings.FromName),
            Subject = outgoing.Subject,
            Body = outgoing.HtmlBody,
            IsBodyHtml = true
        };

        message.To.Add(outgoing.To);

        if (outgoing.ReplyTo is not null)
        {
            message.ReplyToList.Add(outgoing.ReplyTo);
        }

        foreach ((string name, string value) in outgoing.Headers)
        {
            message.Headers.Add(name, value);
        }

        return message;
    }
}

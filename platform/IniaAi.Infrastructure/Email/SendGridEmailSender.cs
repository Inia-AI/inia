using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Configuration.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace IniaAi.Infrastructure.Email;

/// <summary>
/// Provides functionality for sending emails via the SendGrid service.
/// </summary>
public class SendGridEmailSender : IEmailSender
{
    private readonly string _apiKey;
    private readonly string _fromEmail;
    private readonly string _fromName;
    private readonly ILogger<SendGridEmailSender> _logger;
    private readonly HttpClient? _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="SendGridEmailSender"/> class.
    /// </summary>
    /// <param name="options">The configured SendGrid settings.</param>
    /// <param name="logger">Logger instance for logging operations.</param>
    public SendGridEmailSender(IOptions<SendGridSettings> options, ILogger<SendGridEmailSender> logger)
    {
        _apiKey = options.Value.ApiKey;
        _fromEmail = options.Value.FromEmail;
        _fromName = options.Value.FromName;
        _logger = logger;
    }

    /// <summary>Sends through <paramref name="httpClient"/>, so a test answers as SendGrid would.</summary>
    internal SendGridEmailSender(IOptions<SendGridSettings> options, ILogger<SendGridEmailSender> logger, HttpClient httpClient)
        : this(options, logger)
    {
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task SendEmailAsync(OutgoingMailMessage message)
    {
        SendGridClient client = _httpClient is null ? new SendGridClient(_apiKey) : new SendGridClient(_httpClient, _apiKey);
        var from = new EmailAddress(_fromEmail, _fromName);
        SendGridMessage msg = MailHelper.CreateSingleEmail(from, new EmailAddress(message.To), message.Subject, null, message.HtmlBody);

        if (message.ReplyTo is not null)
        {
            msg.ReplyTo = new EmailAddress(message.ReplyTo);
        }

        foreach ((string name, string value) in message.Headers)
        {
            msg.AddHeader(name, value);
        }

        Response response = await client.SendEmailAsync(msg);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"SendGrid refused the mail with status {(int)response.StatusCode}.");
        }

        _logger.LogInformation("SendGrid email sent, status: {StatusCode}", response.StatusCode);
    }
}

using System.Buffers;
using System.Globalization;
using System.Text;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Configuration.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IniaAi.Infrastructure.Email;

/// <summary>
/// Sends through the Amazon SES API with the host's own AWS credentials, so no SMTP user or stored
/// key exists. An account in the SES sandbox delivers only to verified addresses.
/// </summary>
public sealed class SesEmailSender : IEmailSender
{
    private const string CHARSET = "UTF-8";

    private static readonly SearchValues<char> AddressSpecials = SearchValues.Create("()<>[]:;@\\,.\"");

    private readonly SesSettings _settings;
    private readonly ILogger<SesEmailSender> _logger;
    // The client resolves its AWS region when it is built, so a host that never mails needs no AWS
    // configuration, and a build that fails is tried again on the next mail.
    private readonly Lazy<IAmazonSimpleEmailServiceV2> _client = new(() => new AmazonSimpleEmailServiceV2Client(), LazyThreadSafetyMode.PublicationOnly);

    public SesEmailSender(IOptions<SesSettings> options, ILogger<SesEmailSender> logger)
    {
        _settings = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SendEmailAsync(OutgoingMailMessage message)
    {
        SendEmailResponse response = await _client.Value.SendEmailAsync(Compose(message, _settings));
        _logger.LogInformation("SES email sent, message id: {MessageId}", response.MessageId);
    }

    /// <summary>The SES request for <paramref name="message"/>, from the configured sender.</summary>
    public static SendEmailRequest Compose(OutgoingMailMessage message, SesSettings settings)
    {
        return new SendEmailRequest
        {
            FromEmailAddress = string.IsNullOrWhiteSpace(settings.FromName)
                ? settings.FromEmail
                : $"{DisplayName(settings.FromName)} <{settings.FromEmail}>",
            Destination = new Destination { ToAddresses = [AsciiDomain(message.To)] },
            ReplyToAddresses = message.ReplyTo is null ? [] : [AsciiDomain(message.ReplyTo)],
            Content = new EmailContent
            {
                Simple = new Message
                {
                    Subject = new Content { Data = message.Subject, Charset = CHARSET },
                    Body = new Body { Html = new Content { Data = message.HtmlBody, Charset = CHARSET } },
                    Headers = [.. message.Headers.Select(header => new MessageHeader { Name = header.Key, Value = header.Value })],
                },
            },
        };
    }

    /// <summary>
    /// The sender's name as a mail header may carry it: as it is when it is plain text, quoted when it
    /// holds an address character, and encoded (RFC 2047) when it is not ASCII, as SES requires.
    /// </summary>
    private static string DisplayName(string name)
    {
        if (!Ascii.IsValid(name))
        {
            return $"=?utf-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(name))}?=";
        }

        return name.AsSpan().ContainsAny(AddressSpecials)
            ? $"\"{name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : name;
    }

    /// <summary>The address with its domain in ASCII (Punycode), the only form SES accepts.</summary>
    private static string AsciiDomain(string address)
    {
        int at = address.LastIndexOf('@');
        return at < 0 || Ascii.IsValid(address.AsSpan(at + 1))
            ? address
            : $"{address[..at]}@{new IdnMapping().GetAscii(address[(at + 1)..])}";
    }
}

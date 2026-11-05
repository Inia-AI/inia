namespace IniaAi.Application.Common.Interfaces.Services;

/// <summary>
/// The deployment's outgoing mail. A feature that mails checks <see cref="IsConfigured"/> first
/// and stays off until a provider is set up, so no screen promises a mail that cannot go.
/// </summary>
public interface IOutgoingMail
{
    /// <summary>
    /// Whether the deployment named a mail provider and gave it what it needs to send.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Sends one HTML mail through the configured provider. Refuses when mail is off.
    /// </summary>
    Task SendAsync(string to, string subject, string htmlBody)
    {
        return SendAsync(new OutgoingMailMessage(to, subject, htmlBody));
    }

    /// <summary>
    /// Sends one HTML mail with its reply-to address and headers, after stripping CR and LF from
    /// every header value. Refuses when mail is off.
    /// </summary>
    Task SendAsync(OutgoingMailMessage message);
}

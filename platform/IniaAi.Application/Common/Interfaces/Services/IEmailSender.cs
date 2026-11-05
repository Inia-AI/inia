namespace IniaAi.Application.Common.Interfaces.Services;

/// <summary>
/// Defines an abstraction for sending emails.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Sends one HTML mail with its reply-to address and headers. Only <c>OutgoingMail</c> calls it,
    /// after stripping line breaks from every header value.
    /// </summary>
    /// <param name="message">The mail.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SendEmailAsync(OutgoingMailMessage message);
}

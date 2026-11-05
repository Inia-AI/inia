namespace IniaAi.Configuration.Email;

/// <summary>
/// Configuration settings for sending through Amazon SES. The sender signs in with the host's own
/// AWS credentials, so these settings hold no key; the host's role needs <c>ses:SendEmail</c> on the
/// sending identity, and the AWS region comes from the host, as for the platform's other AWS calls.
/// </summary>
public class SesSettings
{
    public const string SECTION_NAME = "Ses";

    /// <summary>
    /// The email address that will appear as the sender. Its domain must be a verified SES identity.
    /// </summary>
    public string FromEmail { get; set; } = string.Empty;

    /// <summary>
    /// The name that will appear as the sender.
    /// </summary>
    public string FromName { get; set; } = string.Empty;
}

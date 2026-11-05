namespace IniaAi.Configuration.Email;

/// <summary>
/// Configuration settings for SendGrid email provider.
/// </summary>
public class SendGridSettings
{
    public const string SECTION_NAME = "SendGrid";

    /// <summary>
    /// The API key used to authenticate with SendGrid.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The email address that will appear as the sender.
    /// </summary>
    public string FromEmail { get; set; } = string.Empty;

    /// <summary>
    /// The name that will appear as the sender.
    /// </summary>
    public string FromName { get; set; } = string.Empty;
}

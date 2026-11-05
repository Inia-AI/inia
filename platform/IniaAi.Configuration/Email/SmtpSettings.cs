namespace IniaAi.Configuration.Email;

/// <summary>
/// Configuration settings for SMTP email provider.
/// </summary>
public class SmtpSettings
{
    public const string SECTION_NAME = "Smtp";

    /// <summary>
    /// The SMTP server host.
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// The port used for the SMTP server.
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// Whether to enable SSL for SMTP connection.
    /// </summary>
    public bool EnableSsl { get; set; }

    /// <summary>
    /// The username used to authenticate with the SMTP server.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// The password used to authenticate with the SMTP server.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// The email address that will appear as the sender.
    /// </summary>
    public string FromEmail { get; set; } = string.Empty;

    /// <summary>
    /// The name that will appear as the sender.
    /// </summary>
    public string FromName { get; set; } = string.Empty;
}

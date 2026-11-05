namespace IniaAi.Configuration.Messaging;

public class MessageProcessorSettings
{
    /// <summary>
    /// API key that admits a service client to the file download endpoint, and to any endpoint a
    /// module guards with it, without a signed-in user. Empty by default, which turns key access
    /// off: the endpoints then admit only the roles they name.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}

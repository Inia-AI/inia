namespace IniaAi.AiConnection.Abstractions;

/// <summary>
/// A model gateway that a closed module supplies for <see cref="Configuration.Platform.DeploymentMode.Gateway"/>.
/// The chat connector hands it a Bedrock InvokeModel call, and the gateway wraps the call in its own
/// request format and reads its own reply format.
/// </summary>
public interface IModelGateway
{
    /// <summary>
    /// The refusal when the gateway switch is on and no closed module supplies a gateway.
    /// </summary>
    public const string NOT_IN_BUILD =
        "PlatformSettings:UsesModelGateway is on, but this build has no model gateway. "
        + "Turn the setting off, and model calls go to the cloud this host runs in, or to the public APIs.";

    /// <summary>
    /// The gateway's name in the connector's log lines.
    /// </summary>
    string Name { get; }

    string ChatUrl { get; }

    /// <summary>
    /// Whether calls to the gateway skip the system proxy.
    /// </summary>
    bool BypassesProxy { get; }

    int ReadTimeoutSeconds(bool isThinking);

    /// <summary>
    /// Signs one call: adds the headers that authenticate it.
    /// </summary>
    Task AddCallHeadersAsync(IDictionary<string, string> headers, CancellationToken cancellationToken);

    /// <summary>
    /// The request the gateway reads for one Bedrock InvokeModel call.
    /// </summary>
    string WrapRequest(string bedrockBody, string modelId, int readTimeoutSeconds);

    /// <summary>
    /// The Bedrock response body inside a successful reply, or null when the reply holds none.
    /// </summary>
    string? UnwrapResponse(string reply);

    /// <summary>
    /// The error text of a refusal in the gateway's own format, or null when the reply is not in it.
    /// </summary>
    string? ReadErrorText(string reply);
}

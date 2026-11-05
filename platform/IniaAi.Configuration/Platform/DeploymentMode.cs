namespace IniaAi.Configuration.Platform;

/// <summary>
/// Where the platform's model calls go, decided once per host by <see cref="CloudHost.ModeFor"/>
/// and read through <see cref="PlatformSettings.DeploymentMode"/>. Every switch that picks a
/// connector, an embeddings connector or the agent runtime's model provider reads this one value.
/// </summary>
public enum DeploymentMode
{
    /// <summary>A model gateway that a closed module supplies, whatever cloud the host runs in.</summary>
    Gateway,

    /// <summary>Azure AI Foundry.</summary>
    Azure,

    /// <summary>AWS Bedrock.</summary>
    Aws,

    /// <summary>The Anthropic and Cohere public APIs.</summary>
    DirectApi,
}

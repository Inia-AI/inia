using System.ComponentModel;

namespace IniaAi.AiConnection.AwsBedrock;

/// <summary>
/// Represents the different model families available on AWS Bedrock.
/// </summary>
public enum ModelFamily
{
    [Description("Anthropic Claude models")]
    Anthropic,

    [Description("Cohere Command models")]
    Cohere
}

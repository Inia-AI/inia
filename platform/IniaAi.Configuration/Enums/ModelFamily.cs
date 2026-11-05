using System.ComponentModel;

namespace IniaAi.Configuration.Enums;

/// <summary>
/// Represents the different model families available for AI operations.
/// Note: This enum is duplicated from IniaAi.AiConnection.AwsBedrock.ModelFamily
/// to avoid circular dependencies in the Configuration layer.
/// </summary>
public enum ModelFamily
{
    [Description("Anthropic Claude models")]
    Anthropic,

    [Description("Cohere Command models")]
    Cohere
}

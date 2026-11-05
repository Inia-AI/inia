namespace IniaAi.AiConnection.Anthropic.ApiModel;

/// <summary>
/// The sampling and thinking knobs shared by <see cref="RequestBody"/> (public API)
/// and <see cref="Bedrock.BedrockRequestBody"/> (Bedrock InvokeModel), so the
/// model-dependent request shape can be applied by one implementation.
/// </summary>
internal interface ITunableRequestBody
{
    int MaxTokens { get; }

    double? Temperature { get; set; }

    double? TopP { get; set; }

    int? TopK { get; set; }

    ThinkingSettings? ThinkingSettings { get; set; }

    OutputConfig? OutputConfig { get; set; }
}

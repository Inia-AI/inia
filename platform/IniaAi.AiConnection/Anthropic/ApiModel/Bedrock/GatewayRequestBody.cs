using IniaAi.Common.Extensions;
using IniaAi.Configuration.Enums.Anthropic;

namespace IniaAi.AiConnection.Anthropic.ApiModel.Bedrock;

/// <summary>
/// A Bedrock InvokeModel call bound for a model gateway, which wraps it in its own request format.
/// </summary>
internal sealed class GatewayRequestBody(BedrockRequestBody requestBody)
{
    public BedrockRequestBody RequestBody { get; } = requestBody;

    public AiModel ModelId { get; set; } = AiModel.UsAwsSonnet4_6;

    public string? ModelOverride { get; set; }

    public int ReadTimeoutSeconds { get; set; }

    public string ModelName => ModelOverride ?? ModelId.GetEnumMember();

    public override string ToString()
    {
        return $"ModelId: {ModelName}, Body: {RequestBody}";
    }
}

using IniaAi.Common.Serialization;
using Newtonsoft.Json;

namespace IniaAi.AiConnection.Anthropic.ApiModel;

/// <summary>
/// Usage interface
/// </summary>
internal interface IUsage
{
    /// <summary>
    /// The number of input tokens which were used in the request.
    /// </summary>
    int InputTokens { get; set; }

    /// <summary>
    /// The number of output tokens which were used in the request.
    /// </summary>
    int OutputTokens { get; set; }

    int CacheCreationInputTokens { get; set; }

    int CacheReadInputTokens { get; set; }
}

/// <summary>
/// Billing and rate-limit usage.
/// <para>
/// Anthropic's API bills and rate-limits by token counts, as tokens
/// represent the underlying cost to our systems.
/// </para>
/// <para>
/// Under the hood, the API transforms requests into a format suitable
/// for the model. The model's output then goes through a parsing stage
/// before becoming an API response. As a result, the token counts in
/// usage will not match one-to-one with the exact visible content of an
/// API request or response.
/// </para>
/// <para>
/// For example, output_tokens will be non-zero,
/// even for an empty string response from Claude.
/// </para>
/// </summary>
[JsonObject(MemberSerialization.OptIn)]
internal class Usage : JsonSerializable<Usage>, IUsage
{
    /// <inheritdoc/>
    [JsonProperty("input_tokens")]
    public int InputTokens { get; set; } = 0;

    /// <inheritdoc/>
    [JsonProperty("output_tokens")]
    public int OutputTokens { get; set; } = 0;

    /// <inheritdoc/>
    [JsonProperty("cache_creation_input_tokens")]
    public int CacheCreationInputTokens { get; set; } = 0;

    /// <inheritdoc/>
    [JsonProperty("cache_read_input_tokens")]
    public int CacheReadInputTokens { get; set; } = 0;
}

/// <summary>
/// AWS usage
/// </summary>
[JsonObject(MemberSerialization.OptIn)]
internal class AWSUsage : JsonSerializable<AWSUsage>, IUsage
{
    /// <inheritdoc/>
    [JsonProperty("inputTokenCount")]
    public int InputTokens { get; set; } = 0;

    /// <inheritdoc/>
    [JsonProperty("outputTokenCount")]
    public int OutputTokens { get; set; } = 0;

    /// <summary>
    /// Invocation latency
    /// </summary>
    [JsonProperty("invocationLatency")]
    public int InvocationLatency { get; set; } = 0;

    /// <summary>
    /// First-byte latency
    /// </summary>
    [JsonProperty("firstByteLatency")]
    public int FirstByteLatency { get; set; } = 0;

    /// <inheritdoc/>
    [JsonProperty("cacheWriteInputTokenCount")]
    public int CacheCreationInputTokens { get; set; } = 0;

    /// <inheritdoc/>
    [JsonProperty("cacheReadInputTokenCount")]
    public int CacheReadInputTokens { get; set; } = 0;
}

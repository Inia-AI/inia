using IniaAi.AiConnection.Anthropic.ApiModel.Enums;
using IniaAi.Common.Serialization;
using Newtonsoft.Json;

namespace IniaAi.AiConnection.Anthropic.ApiModel.Bedrock;

/// <summary>
/// The request body for the Anthropic API.
/// https://docs.aws.amazon.com/bedrock/latest/userguide/model-parameters-anthropic-claude-messages.html
/// </summary>
internal class BedrockRequestBody : JsonSerializable<BedrockRequestBody>, IMessagesContainer, ITunableRequestBody
{
    private const int DEFAULT_MAX_TOKENS = 4096;

    /// <summary>
    /// The anthropic version. The value must be bedrock-2023-05-31
    /// </summary>
    [JsonProperty("anthropic_version")]
    public string AnthropicVersion { get; set; } = "bedrock-2023-05-31";

    [JsonProperty("anthropic_beta")]
    public List<string> AnthropicBeta { get; set; } = [];

    public bool ShouldSerializeAnthropicBeta()
    {
        return AnthropicBeta.Count > 0;
    }

    /// <summary>
    /// The input messages.
    /// </summary>
    [JsonProperty("messages")]
    public List<Message> Messages { get; private set; } = [];

    /// <summary>
    /// The maximum number of tokens to generate before stopping. Note that
    /// Anthropic Claude models might stop generating tokens before reaching
    /// the value of <paramref name="MaxTokens"/>. Different Anthropic Claude
    /// models have different maximum values for this parameter.
    /// For more information, see Model comparison:
    /// https://docs.anthropic.com/claude/docs/models-overview#model-comparison
    /// </summary>
    [JsonProperty("max_tokens")]
    public int MaxTokens { get; set; } = DEFAULT_MAX_TOKENS;

    /// <summary>
    /// The thinking configuration for the request. Legacy models use type
    /// "enabled" with a budget_tokens cap that must stay below max_tokens;
    /// adaptive-generation models accept only "adaptive" (for thinking requests)
    /// or "disabled" ("between_tools" on Sonnet 5.5), and reject budget_tokens.
    /// See <see cref="ThinkingSettings"/>.
    /// </summary>
    [JsonProperty("thinking", NullValueHandling = NullValueHandling.Ignore)]
    public ThinkingSettings? ThinkingSettings { get; set; }

    /// <summary>
    /// The effort configuration for the request. Only adaptive-generation
    /// models accept it; <see cref="ModelRequestShaper"/> is the sole writer.
    /// </summary>
    [JsonProperty("output_config", NullValueHandling = NullValueHandling.Ignore)]
    public OutputConfig? OutputConfig { get; set; }

    /// <summary>
    /// System prompt.
    /// A system prompt is a way of providing context and instructions to
    /// Claude, such as specifying a particular goal or role.
    /// For more information, see System prompts in the Anthropic documentation:
    /// https://docs.anthropic.com/en/docs/system-prompts
    /// </summary>
    [JsonProperty("system", NullValueHandling = NullValueHandling.Ignore)]
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// Custom text sequences that cause the model to stop generating.
    /// Anthropic Claude models normally stop when they have naturally completed
    /// their turn, in this case the value of the <seealso cref="StopReasonType"/>
    /// response field is <code>end_turn</code>
    ///
    /// If you want the model to stop
    /// generating when it encounters custom strings of text, you can use the
    /// <seealso cref="StopSequences"/> parameter. If the model encounters one of
    /// the custom text strings, the value of the <seealso cref="StopReasonType"/>
    /// response field is <seealso cref="ResponseBody.StopSequence"/> and the
    /// value of <seealso cref="ResponseBody.StopSequence"/> contains the
    /// matched stop sequence. The maximum number of entries is 8191.
    /// </summary>
    [JsonProperty("stop_sequences", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? StopSequences { get; set; }

    /// <summary>
    /// Amount of randomness injected into the response. Defaults to 1.0.
    /// Ranges from 0.0 to 1.0. Use temperature closer to 0.0 for
    /// analytical/multiple choice, and closer to 1.0 for creative and
    /// generative tasks. Note that even with temperature of 0.0, the results
    /// will not be fully deterministic.
    /// </summary>
    [JsonProperty("temperature", NullValueHandling = NullValueHandling.Ignore)]
    public double? Temperature { get; set; } = 0.2;

    /// <summary>
    /// Use nucleus sampling. In nucleus sampling, we compute the cumulative
    /// distribution over all the options for each subsequent token in
    /// decreasing probability order and cut it off once it reaches a
    /// particular probability specified by top_p. You should either alter
    /// temperature or top_p, but not both. Recommended for advanced use cases
    /// only. You usually only need to use temperature.
    /// </summary>
    [JsonProperty("top_p", NullValueHandling = NullValueHandling.Ignore)]
    public double? TopP { get; set; }

    /// <summary>
    /// Only sample from the top K options for each subsequent token.
    /// Used to remove "long tail" low probability responses. Learn more
    /// technical details here. Recommended for advanced use cases only.
    /// You usually only need to use temperature.
    /// </summary>
    [JsonProperty("top_k", NullValueHandling = NullValueHandling.Ignore)]
    public int? TopK { get; set; }

    public BedrockRequestBody()
    {
    }

    public BedrockRequestBody(
        Message message,
        int maxTokens = -1,
        string? systemPrompt = null)
    {
        Messages.Add(message);
        SystemPrompt = string.IsNullOrEmpty(systemPrompt)
            ? null
            : systemPrompt;

        if (maxTokens > 0)
        {
            MaxTokens = maxTokens;
        }
    }

    public override string ToString()
    {
        string str = $"Max tokens: {MaxTokens}, {Messages.Count} messages.";

        for (int i = 0; i < Messages.Count; i++)
        {
            str += $"\nMessage {i} content: " +
                $"{Messages[i].Role} role, " +
                $"{Messages[i].Content.Count(c => c.Type is ContentType.Text)} text content, " +
                $"{Messages[i].Content.Count(c => c.Type is ContentType.Image)} image content";
        }

        return str;
    }
}

using IniaAi.AiConnection.Anthropic.ApiModel.Enums;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Common;
using IniaAi.Common.Extensions;
using IniaAi.Common.Serialization;
using IniaAi.Configuration.Enums.Anthropic;
using Newtonsoft.Json;

namespace IniaAi.AiConnection.Anthropic.ApiModel;

/// <summary>
/// The request body for the Anthropic API.
/// </summary>
internal class RequestBody : JsonSerializable<RequestBody>, IMessagesContainer, ITunableRequestBody
{
    private const int DEFAULT_MAX_TOKENS = 8192;

    /// <summary>
    /// System prompt.
    /// A system prompt is a way of providing context and instructions to
    /// Claude, such as specifying a particular goal or role.
    /// </summary>
    [JsonProperty("system", NullValueHandling = NullValueHandling.Ignore)]
    public List<ContentRequest>? SystemPrompt { get; set; }

    /// <summary>
    /// Input messages.
    /// </summary>
    [JsonProperty("messages")]
    public List<Message> Messages { get; private set; } = [];

    /// <summary>
    /// The maximum number of tokens to generate before stopping. This
    /// parameter only specifies the absolute maximum number of tokens to
    /// generate. Different models have different maximum values for this
    /// parameter
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
    /// The model that will complete your prompt. Settable using <seealso cref="AiModel"/>.
    /// The fallback stays a non-adaptive model so a bare request is valid with the
    /// default <see cref="Temperature"/>, which adaptive-generation models reject;
    /// the connector overrides this with the configured tier for real requests.
    /// </summary>
    [JsonIgnore]
    public AiModel? Model { get; set; } = AiModel.PublicSonnet4_6;

    /// <summary>
    /// The ID of the model that will complete your prompt. This is useful
    /// when you want to specify a specific model to use for a request and
    /// need to override default model IDs.
    /// </summary>
    [JsonIgnore]
    public string? ModelOverride { get; set; }

    [JsonProperty("model", NullValueHandling = NullValueHandling.Ignore)]
    public string? SerializedModel
    {
        get => ModelOverride ?? Model?.GetEnumMember();
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                Model = null;
                ModelOverride = null;

                return;
            }

            if (Utils.TryParseEnumFromEnumMemberAttribute(value, out AiModel result))
            {
                Model = result;
                ModelOverride = null;

                return;
            }

            ModelOverride = value;
            Model = null;
        }
    }

    /// <summary>
    /// An object describing metadata about the request.
    /// </summary>
    [JsonProperty("metadata", NullValueHandling = NullValueHandling.Ignore)]
    public Metadata? Metadata { get; set; }

    /// <summary>
    /// Custom text sequences that will cause the model to stop generating. Our
    /// models will normally stop when they have naturally completed their turn,
    /// which will result in a response stop_reason of "end_turn". If you want
    /// the model to stop generating when it encounters custom strings of text,
    /// you can use the stop_sequences parameter. If the model encounters one of
    /// the custom sequences, the response stop_reason value will be "stop_sequence"
    /// and the response stop_sequence value will contain the matched stop sequence.
    /// </summary>
    [JsonProperty("stop_sequences", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? StopSequences { get; set; }

    /// <summary>
    /// Whether to incrementally stream the response using server-sent events.
    /// </summary>
    [JsonProperty("stream", NullValueHandling = NullValueHandling.Ignore)]
    public bool? Stream { get; set; }

    /// <summary>
    /// Anthropic version, required to be 'bedrock-2023-05-31'
    /// </summary>
    /// <remarks>See https://docs.aws.amazon.com/bedrock/latest/userguide/model-parameters-anthropic-claude-messages.html</remarks>
    [JsonProperty("anthropic_version", NullValueHandling = NullValueHandling.Ignore)]
    public string? AnthropicVersion { get; set; } = null;

    /// <summary>
    /// Beta headers used to indicate opt-in to a particular set of beta features.
    /// </summary>
    [JsonProperty("anthropic_beta", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? AnthropicBeta { get; set; }

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

    public RequestBody()
    {
    }

    public RequestBody(
        Message message,
        int maxTokens = -1,
        string? systemPrompt = null)
    {
        Messages.Add(message);

        if (maxTokens > 0)
        {
            MaxTokens = maxTokens;
        }

        if (string.IsNullOrWhiteSpace(systemPrompt))
        {
            return;
        }

        SystemPrompt ??= [];
        SystemPrompt.Add(new ContentRequest(new RequestCommonContent(systemPrompt)));
    }

    public override string ToString()
    {
        string str = $"Model: {Model}, Max tokens: {MaxTokens}, {Messages.Count} messages.";

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

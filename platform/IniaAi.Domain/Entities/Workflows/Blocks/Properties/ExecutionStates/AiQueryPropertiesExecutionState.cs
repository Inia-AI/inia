using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates.Abstractions;
using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;

/// <summary>
/// Execution state for AiQueryProperties. Holds resolved prompt values and cache text.
/// </summary>
public class AiQueryPropertiesExecutionState : BlockPropertiesExecutionStateBase<AiQueryPropertiesExecutionState>
{
    [JsonProperty(nameof(UserPrompt), NullValueHandling = NullValueHandling.Ignore)]
    public string? UserPrompt { get; set; }

    [JsonProperty(nameof(SystemPrompt), NullValueHandling = NullValueHandling.Ignore)]
    public string? SystemPrompt { get; set; }

    [JsonProperty(nameof(TextToCache), NullValueHandling = NullValueHandling.Ignore)]
    public string? TextToCache { get; set; }

    [JsonProperty(nameof(UserPromptPreview), NullValueHandling = NullValueHandling.Ignore)]
    public string? UserPromptPreview { get; set; }

    [JsonProperty(nameof(ModelIdOverride), NullValueHandling = NullValueHandling.Ignore)]
    public string? ModelIdOverride { get; set; }

    /// <summary>
    /// The model id the connector sent this run's call to, when it reports one, such as the
    /// deployment's own Bedrock profile for a stored id of another region.
    /// </summary>
    [JsonProperty(nameof(ModelCalled), NullValueHandling = NullValueHandling.Ignore)]
    public string? ModelCalled { get; set; }

    [JsonProperty(nameof(ConditionalTag), NullValueHandling = NullValueHandling.Ignore)]
    public string? ConditionalTag { get; set; }

    [JsonProperty(nameof(InputContext), NullValueHandling = NullValueHandling.Ignore)]
    public string? InputContext { get; set; }

    /// <summary>
    /// Resolved values used for each <see cref="AiQueryProperties.SignatureFields"/> placeholder
    /// when this block executed. Captured post-substitution so the signature-replay feature can
    /// know the exact substring that landed inside <see cref="UserPrompt"/> / <see cref="SystemPrompt"/>.
    /// Null on pre-capture executions — those runs can't be replayed.
    /// </summary>
    [JsonProperty(nameof(ResolvedSignatureFieldValues), NullValueHandling = NullValueHandling.Ignore)]
    public Dictionary<string, string>? ResolvedSignatureFieldValues { get; set; }

    [JsonConstructor]
    [System.Text.Json.Serialization.JsonConstructor]
    internal AiQueryPropertiesExecutionState()
    {
    }

    public AiQueryPropertiesExecutionState(AiQueryProperties source)
    {
        UserPrompt = source.UserPromptTemplate;
        SystemPrompt = source.SystemPromptTemplate;
        TextToCache = source.TextToCache;
        UserPromptPreview = source.UserPromptTemplatePreview;
        ModelIdOverride = source.ModelIdOverride;
        ConditionalTag = source.ConditionalTagTemplate;
    }
}

using IniaAi.Common.Enums;
using IniaAi.Common.Reflection;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties;

/// <summary>
/// Properties for AI Query blocks that use LLMs for document analysis.
/// Inherits RAG capabilities for document retrieval.
/// </summary>
public class AiQueryProperties : RagBlockProperties, IHasSignatureFields
{
    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the AI user prompt. It can contain placeholders that get
    /// replaced at execution time. Use
    /// <see cref="AiQueryPropertiesExecutionState.UserPrompt"/> for the
    /// actual user prompt after replacements.
    /// </summary>
    [JsonProperty(nameof(UserPromptTemplate), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? UserPromptTemplate { get; set; }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the user-prompt preview. It can contain placeholders
    /// that get replaced at execution time. Use
    /// <see cref="AiQueryPropertiesExecutionState.UserPromptPreview"/> for
    /// the actual user-prompt preview after replacements.
    /// </summary>
    [JsonProperty(nameof(UserPromptTemplatePreview), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [IniaAi.Common.Reflection.NotAReplacementTemplate]
    public string? UserPromptTemplatePreview { get; set; }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the AI system prompt. It can contain placeholders that
    /// get replaced at execution time. Use
    /// <see cref="AiQueryPropertiesExecutionState.SystemPrompt"/> for the
    /// actual system prompt after replacements.
    /// </summary>
    [JsonProperty(nameof(SystemPromptTemplate), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? SystemPromptTemplate { get; set; }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the text to cache. It can contain placeholders that get
    /// replaced at execution time. Use
    /// <see cref="AiQueryPropertiesExecutionState.TextToCache"/> for the
    /// actual cached text after replacements.
    /// </summary>
    [JsonProperty(nameof(TextToCache), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? TextToCache { get; set; }

    /// <summary>
    /// The longest answer the model may write, in tokens. An answer that reaches it is cut off.
    /// </summary>
    [JsonProperty(nameof(MaxTokens))]
    public int MaxTokens { get; set; } = 8000;

    /// <summary>
    /// Uses the deployment's advanced model when on, its basic model when off. ModelIdOverride, when set, wins over both.
    /// </summary>
    [JsonProperty(nameof(ShouldUseAdvancedModel))]
    public bool ShouldUseAdvancedModel { get; set; } = true;

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the model-id override. It can contain placeholders that
    /// get replaced at execution time. Use
    /// <see cref="AiQueryPropertiesExecutionState.ModelIdOverride"/> for the
    /// actual model id after replacements.
    /// </summary>
    [JsonProperty(nameof(ModelIdOverride), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? ModelIdOverride { get; set; }

    /// <summary>
    /// Splits the retrieved document pages into chunks and asks the model once per chunk, then joins the answers. Only applies when the block has retrieval settings and documents.
    /// </summary>
    [JsonProperty(nameof(ShouldChunkLargeInputContexts))]
    public bool ShouldChunkLargeInputContexts { get; set; }

    public bool ShouldSerializeShouldChunkLargeInputContexts()
    {
        return ShouldChunkLargeInputContexts;
    }

    /// <summary>
    /// How many pages go into each chunk when chunking by page count.
    /// </summary>
    [JsonProperty(nameof(ChunkSize))]
    public int ChunkSize { get; set; } = 20;

    public bool ShouldSerializeChunkSize()
    {
        return ShouldChunkLargeInputContexts && ChunkingMode is ChunkingMode.FixedPageCount;
    }

    /// <summary>
    /// The chunking mode to use when ShouldChunkLargeInputContexts is true.
    /// </summary>
    [JsonProperty(nameof(ChunkingMode))]
    [JsonConverter(typeof(StringEnumConverter))]
    public ChunkingMode ChunkingMode { get; set; } = ChunkingMode.FixedPageCount;

    public bool ShouldSerializeChunkingMode()
    {
        return ShouldChunkLargeInputContexts;
    }

    /// <summary>
    /// Maximum characters allowed in each chunk when using OptimalCharacters mode.
    /// </summary>
    [JsonProperty(nameof(MaxChunkCharacters))]
    public int MaxChunkCharacters { get; set; } = 50000;

    public bool ShouldSerializeMaxChunkCharacters()
    {
        return ShouldChunkLargeInputContexts && ChunkingMode is ChunkingMode.OptimalCharacters;
    }

    /// <summary>
    /// When true, pages must remain in sequential order during optimal chunking.
    /// When false, pages can be reordered to optimize packing efficiency.
    /// </summary>
    [JsonProperty(nameof(PreservePageOrder))]
    public bool PreservePageOrder { get; set; } = true;

    public bool ShouldSerializePreservePageOrder()
    {
        return ShouldChunkLargeInputContexts && ChunkingMode is ChunkingMode.OptimalCharacters;
    }

    /// <summary>
    /// Also sends the document pages as images. Only applies with the advanced model.
    /// </summary>
    [JsonProperty(nameof(ShouldUsePageImages))]
    public bool ShouldUsePageImages { get; set; }

    /// <summary>
    /// Lets the model think before it answers, up to ThinkingBudget tokens. Falls back to a normal answer where the model connection does not support it.
    /// </summary>
    [JsonProperty(nameof(ShouldUseThinkingMode))]
    public bool ShouldUseThinkingMode { get; set; }

    public bool ShouldSerializeShouldUseThinkingMode()
    {
        return ShouldUseThinkingMode;
    }

    /// <summary>
    /// The most tokens the model may spend thinking when ShouldUseThinkingMode is on.
    /// </summary>
    [JsonProperty(nameof(ThinkingBudget))]
    public int ThinkingBudget { get; set; } = 1024;

    public bool ShouldSerializeThinkingBudget()
    {
        return ShouldUseThinkingMode;
    }

    /// <summary>
    /// The effort level sent to adaptive-generation models as
    /// <c>output_config.effort</c>. Null keeps the model's API default (high on
    /// most models, medium on Opus 5.5 and Haiku 5.5); models without effort support ignore
    /// the value.
    /// </summary>
    [JsonProperty(nameof(Effort), NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(StringEnumConverter))]
    public ModelEffort? Effort { get; set; }

    /// <summary>
    /// Caches the document content between calls, so repeated calls on the same pages cost less. Only where the model connection supports caching.
    /// </summary>
    [JsonProperty(nameof(ShouldUseCachingForInputContext))]
    public bool ShouldUseCachingForInputContext { get; set; }

    public bool ShouldSerializeShouldUseCachingForInputContext()
    {
        return ShouldUseCachingForInputContext;
    }

    /// <summary>
    /// Asks the model to cite the pages it used. Only applies when the block's output reaches a report paragraph or a data output; otherwise the citations would go nowhere.
    /// </summary>
    [JsonProperty(nameof(ShouldGetCitationsFromModel))]
    public bool ShouldGetCitationsFromModel { get; set; }

    public bool ShouldSerializeShouldGetCitationsFromModel()
    {
        return ShouldGetCitationsFromModel;
    }

    /// <summary>
    /// Sends each page's short overview instead of its full text.
    /// </summary>
    [JsonProperty(nameof(ShouldUseOverviews))]
    public bool ShouldUseOverviews { get; set; }

    public bool ShouldSerializeShouldUseOverviews()
    {
        return ShouldUseOverviews;
    }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the conditional tag. It can contain placeholders that
    /// get replaced at execution time. Use
    /// <see cref="AiQueryPropertiesExecutionState.ConditionalTag"/> for the
    /// actual conditional tag after replacements.
    /// </summary>
    /// <remarks>
    /// Optional XML tag name to check in each chunk's response. If the tag
    /// is found with a non-NA value, processing stops early. Example:
    /// setting this to "answer" will stop when the response contains
    /// &lt;answer&gt;value&lt;/answer&gt;.
    /// </remarks>
    [JsonProperty(nameof(ConditionalTagTemplate), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? ConditionalTagTemplate { get; set; }

    /// <summary>
    /// Inline signature field definitions. On workflow blocks this is LEGACY INPUT ONLY —
    /// <c>SignatureFieldsLegacyMigrator.Hoist</c> moves entries to workflow-level
    /// <c>WorkflowSignatureField</c> definitions on every load/save and nulls the dict. It
    /// remains the permanent storage format for block-template LIBRARY rows (which have no
    /// workflow to hang definitions on); imports hoist it into the target workflow, exports
    /// embed the workflow definitions back into it.
    /// </summary>
    [JsonProperty(nameof(SignatureFields), NullValueHandling = NullValueHandling.Ignore)]
    public Dictionary<string, SignatureFieldDefinition>? SignatureFields { get; set; }

    /// <summary>
    /// Names of the workflow-level signature fields whose tokens this block's templates
    /// reference, recorded by the execution-time injection step so
    /// <see cref="CaptureResolvedSignatureFieldValues"/> can tell signature values apart from
    /// other inputs. Execution-scoped scratch state — never serialized, never cloned.
    /// </summary>
    [JsonIgnore]
    [NotMapped]
    [CloneIgnore]
    public IReadOnlyCollection<string>? CandidateSignatureFieldNames { get; set; }

    // Active override values formerly lived here as a `SignatureFieldValues` dict but were
    // moved to a dedicated `SignatureFieldOverride` table (now keyed by org + workflow type +
    // field name) so workflow JSON exports never carry client data across environments.
    // Run-time resolution happens via `WorkflowHandler.LoadActiveSignatureOverridesAsync` +
    // a batch DB read.

    /// <summary>
    /// Optional structured-output format that the model's response must match. When set to
    /// anything other than <see cref="OutputFormatType.None"/>, the strategy validates the
    /// response and retries once on failure before failing the block.
    /// </summary>
    [JsonProperty(nameof(OutputFormatType))]
    [JsonConverter(typeof(StringEnumConverter))]
    public OutputFormatType OutputFormatType { get; set; } = OutputFormatType.None;

    public bool ShouldSerializeOutputFormatType()
    {
        return OutputFormatType != OutputFormatType.None;
    }

    /// <summary>
    /// Tag name used by <see cref="OutputFormatType.SingleTag"/> and
    /// <see cref="OutputFormatType.JsonInTag"/>. Plain identifier, no attributes, no nesting.
    /// </summary>
    [JsonProperty(nameof(OutputFormatTagName), NullValueHandling = NullValueHandling.Ignore)]
    public string? OutputFormatTagName { get; set; }

    public bool ShouldSerializeOutputFormatTagName()
    {
        return OutputFormatType is OutputFormatType.SingleTag or OutputFormatType.JsonInTag
            && !string.IsNullOrWhiteSpace(OutputFormatTagName);
    }

    /// <summary>
    /// Comma-separated tag names used by <see cref="OutputFormatType.MultiTag"/>. Order
    /// in the response does not matter, every listed tag must be present at least once.
    /// </summary>
    [JsonProperty(nameof(OutputFormatTagNames), NullValueHandling = NullValueHandling.Ignore)]
    public string? OutputFormatTagNames { get; set; }

    public bool ShouldSerializeOutputFormatTagNames()
    {
        return OutputFormatType is OutputFormatType.MultiTag
            && !string.IsNullOrWhiteSpace(OutputFormatTagNames);
    }

    /// <summary>
    /// Optional JSON Schema (Draft 2020-12 compatible) used by <see cref="OutputFormatType.Json"/>
    /// and <see cref="OutputFormatType.JsonInTag"/>. When empty we only check that the
    /// response parses as JSON.
    /// </summary>
    [JsonProperty(nameof(OutputFormatJsonSchema), NullValueHandling = NullValueHandling.Ignore)]
    public string? OutputFormatJsonSchema { get; set; }

    public bool ShouldSerializeOutputFormatJsonSchema()
    {
        return ProducesValidatedJson() && !string.IsNullOrWhiteSpace(OutputFormatJsonSchema);
    }

    /// <summary>
    /// Whether the block's answer is validated as JSON, so its clean JSON lands on
    /// <c>BlockResponse.JsonResponse</c> even when the raw answer is fenced or tag-wrapped.
    /// </summary>
    public bool ProducesValidatedJson()
    {
        return OutputFormatType is OutputFormatType.Json or OutputFormatType.JsonInTag;
    }

    /// <summary>
    /// Number of model attempts the output-format loop ran. 0 when no format is configured,
    /// 1 on a first-attempt success, 2 on a one-retry success, 2 on an exhausted-retry failure.
    /// </summary>
    [JsonProperty(nameof(OutputFormatAttemptCount), NullValueHandling = NullValueHandling.Ignore)]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int OutputFormatAttemptCount { get; set; }

    public bool ShouldSerializeOutputFormatAttemptCount()
    {
        return OutputFormatAttemptCount != 0;
    }

    /// <summary>
    /// Validator error from the most recent failed attempt. Set even on eventual success
    /// when the first attempt failed, so the UI can show "succeeded after 1 retry".
    /// </summary>
    [JsonProperty(nameof(OutputFormatLastError), NullValueHandling = NullValueHandling.Ignore)]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? OutputFormatLastError { get; set; }

    /// <summary>
    /// Raw model response from the most recent failed attempt, captured before the retry
    /// or terminal throw. Lets the UI show the user what the model actually returned that
    /// didn't validate, alongside <see cref="OutputFormatLastError"/>.
    /// </summary>
    [JsonProperty(nameof(OutputFormatFailedAttemptResponse), NullValueHandling = NullValueHandling.Ignore)]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? OutputFormatFailedAttemptResponse { get; set; }

    /// <summary>
    /// Values pulled out of the validated response, keyed by tag or field name.
    /// </summary>
    [JsonProperty(nameof(ExtractedValues), NullValueHandling = NullValueHandling.Ignore)]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? ExtractedValues { get; set; }

    [NotMapped]
    [JsonProperty(nameof(ExecutionState), NullValueHandling = NullValueHandling.Ignore)]
    public AiQueryPropertiesExecutionState? ExecutionState { get; set; }

    public AiQueryProperties()
    {
        Type = BlockTypes.AiQuery;
    }

    public override BlockProperties Clone()
    {
        var clone = new AiQueryProperties();
        CloneHelper.CopyScalars(this, clone);
        return clone;
    }

    /// <summary>
    /// AI Query exposes both the user-prompt and system-prompt templates so
    /// signature placeholders embedded in either count toward the dependency
    /// graph and the "placeholder is unreferenced" warning.
    /// </summary>
    public IEnumerable<string?> GetTemplateSources()
    {
        yield return UserPromptTemplate;
        yield return SystemPromptTemplate;
    }

    /// <summary>
    /// Prompt templates in editor top-to-bottom order: cached section, system, then user.
    /// Includes <see cref="TextToCache"/>, which <see cref="GetTemplateSources"/> omits.
    /// </summary>
    public IEnumerable<string?> GetSignatureOrderingTemplates()
    {
        yield return TextToCache;
        yield return SystemPromptTemplate;
        yield return UserPromptTemplate;
    }

    public override void SetExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState = new AiQueryPropertiesExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
        CaptureResolvedSignatureFieldValues(inputs);
    }

    public override void UpdateExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState ??= new AiQueryPropertiesExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
        CaptureResolvedSignatureFieldValues(inputs);
    }

    // Size guard for pathological prompt-as-signature cases; a single signature field
    // normally holds a few hundred characters of instructions.
    private const int MAX_CAPTURED_VALUE_LENGTH = 64 * 1024;

    // Records the exact resolved-value-per-placeholder dict that fed the template
    // substitution, so the signature-replay feature can find-and-swap the old value
    // inside the captured prompt without ambiguity. Always overwrites
    // ExecutionState.ResolvedSignatureFieldValues on every call — including with
    // null — so re-runs via UpdateExecutionState can't leave a stale dict from a
    // previous execution. Empty strings are skipped because the replay handler
    // can't string-replace on "" (CountOccurrences treats an empty needle as 0),
    // and we don't want to offer the user a Replay button that's guaranteed to
    // fail.
    private void CaptureResolvedSignatureFieldValues(Dictionary<string, string?> inputs)
    {
        if (ExecutionState is null)
        {
            return;
        }

        if (CandidateSignatureFieldNames is null || CandidateSignatureFieldNames.Count == 0)
        {
            ExecutionState.ResolvedSignatureFieldValues = null;
            return;
        }

        Dictionary<string, string>? captured = null;
        foreach (string fieldName in CandidateSignatureFieldNames)
        {
            if (!inputs.TryGetValue(fieldName, out string? value) || value is null)
            {
                continue;
            }

            // Replay can't do anything with an empty value (CountOccurrences("") == 0),
            // so skip it at capture. Empty override values promoted via the pending
            // queue will still round-trip through InjectSignatureFieldValues; they
            // just won't be replayable.
            if (value.Length == 0)
            {
                continue;
            }

            if (value.Length > MAX_CAPTURED_VALUE_LENGTH)
            {
                continue;
            }

            captured ??= [];
            captured[fieldName] = value;
        }

        ExecutionState.ResolvedSignatureFieldValues = captured;
    }

    public override void ClearExecutionState()
    {
        ExecutionState = null;
    }
}

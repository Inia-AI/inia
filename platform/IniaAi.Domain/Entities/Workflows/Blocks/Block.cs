using IniaAi.Common;
using IniaAi.Common.Reflection;
using IniaAi.Common.Serialization;
using IniaAi.Domain.Abstractions.Entities;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Citations.Traces;
using IniaAi.Domain.Common;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Verification;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows.Blocks.ExecutionStates.Abstractions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.Domain.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Document = IniaAi.Domain.Entities.Documents.Document;

namespace IniaAi.Domain.Entities.Workflows.Blocks;

public class Block : Entity<Block>, ITemplatable, ISharedComponentLinkable
{
    /// <summary>
    /// Marks the synthetic per-iteration block that loop expansion emits, whose whole output is the
    /// list item itself. Callers use it to tell "this response is a list item" from "this response is
    /// a model answer".
    /// </summary>
    public const string LOOP_ITEM_STABLE_ID_PREFIX = "loopitem_";

    public const int MAX_REPLACEMENT_TAG_LENGTH = 100;

    private const string DEFAULT_TAG_PREFIX = "block_";

    private const string COPY_TAG_SUFFIX = "_copy";

    [JsonIgnore]
    [CloneIgnore]
    public string? BlockResponseId { get; set; }

    [JsonIgnore]
    [CloneIgnore]
    public string? SectionId { get; set; }

    [JsonIgnore]
    [CloneIgnore]
    public Section? Section { get; set; }

    [JsonProperty(nameof(BlockResponse), NullValueHandling = NullValueHandling.Ignore)]
    [CloneIgnore]
    public BlockResponse? BlockResponse { get; set; }

    [JsonIgnore]
    [CloneIgnore]
    public ICollection<AgentRunTraceFile> AgentRunTraceFiles { get; private set; } = [];

    [JsonIgnore]
    [CloneIgnore]
    public ICollection<AgentRunOutput> AgentRunOutputs { get; private set; } = [];

    [JsonIgnore]
    [CloneIgnore]
    public BlockCitationTrace? CitationTrace { get; set; }

    /// <summary>
    /// Failure-record rows composed at the verifier's settle, carried to the per-block writer.
    /// NotMapped because the record is deliberately relationship-free — a mapped collection
    /// would infer the exact FK the record must not have.
    /// </summary>
    [NotMapped]
    [JsonIgnore]
    [CloneIgnore]
    public ICollection<VerificationCriterionRecord> VerificationCriterionRecords { get; private set; } = [];

    /// <summary>
    /// The data output this run built from the block, carried to the per-block writer with the
    /// block's other rows. NotMapped for the same reason as the verifier's records.
    /// </summary>
    [NotMapped]
    [JsonIgnore]
    [CloneIgnore]
    public ReportDataOutput? DataOutput { get; set; }

    [JsonProperty(nameof(RagSettings))]
    [CloneIgnore]
    public ICollection<RagSettings> RagSettings { get; private set; } = [];

    public bool ShouldSerializeRagSettings()
    {
        return RagSettings.Count > 0;
    }

    [JsonProperty(nameof(TextInputs))]
    [CloneIgnore]
    public ICollection<TextInput> TextInputs { get; private set; } = [];

    public bool ShouldSerializeTextInputs()
    {
        return TextInputs.Count > 0;
    }

    [StringLength(MAX_REPLACEMENT_TAG_LENGTH)]
    [JsonProperty(nameof(ReplacementTag))]
    [CloneIgnore]
    public string ReplacementTag { get; set; } = string.Empty;

    [StringLength(450)]
    [JsonProperty(nameof(Name))]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for generating dynamic output titles. It can contain
    /// placeholders that get replaced at execution time. Use
    /// <see cref="BlockExecutionState.OutputTitle"/> for the actual title after
    /// replacements.
    /// </summary>
    [StringLength(450)]
    [JsonProperty(nameof(OutputTitleTemplate), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? OutputTitleTemplate { get; set; }

    [JsonProperty(nameof(Description), NullValueHandling = NullValueHandling.Ignore)]
    public string? Description { get; set; }

    [JsonProperty(nameof(Order))]
    [CloneIgnore]
    public int Order { get; set; }

    [JsonProperty(nameof(Type))]
    [JsonConverter(typeof(StringEnumConverter))]
    public BlockTypes Type { get; set; } = BlockTypes.AiQuery;

    [JsonProperty(nameof(SupportedDocumentClasses))]
    public List<string> SupportedDocumentClasses { get; set; } = [];

    public bool ShouldSerializeSupportedDocumentClasses()
    {
        return SupportedDocumentClasses.Count > 0;
    }

    [DefaultValue(false)]
    public bool IsOutputSource { get; set; } = false;

    public bool ShouldSerializeIsOutputSource()
    {
        return IsOutputSource;
    }

    /// <summary>
    /// The name another system reads this block's JSON under, beside the report. A block with a
    /// name is a data output; null means it is not one.
    /// </summary>
    [StringLength(MAX_REPLACEMENT_TAG_LENGTH)]
    [JsonProperty(nameof(DataOutputName), NullValueHandling = NullValueHandling.Ignore)]
    public string? DataOutputName { get; set; }

    [NotMapped]
    [JsonIgnore]
    public bool IsDataOutput => !string.IsNullOrWhiteSpace(DataOutputName);

    [DefaultValue(false)]
    public bool ShouldUpdateCounterpartyWithResult { get; set; } = false;

    public bool ShouldSerializeShouldUpdateCounterpartyWithResult()
    {
        return ShouldUpdateCounterpartyWithResult;
    }

    public string? UpdateCounterpartyPropertyKey { get; set; }

    public bool ShouldSerializeUpdateCounterpartyPropertyKey()
    {
        return CounterpartyResultType == Enums.CounterpartyResultType.Property
            && !string.IsNullOrWhiteSpace(UpdateCounterpartyPropertyKey);
    }

    // When set, the scheduler runs this block alone (its own barrier layer), e.g. for blocks with
    // side effects that must not overlap other blocks. See SchedulingViewBuilder.IsBarrier.
    [DefaultValue(false)]
    public bool RunsAlone { get; set; } = false;

    public bool ShouldSerializeRunsAlone()
    {
        return RunsAlone;
    }

    [JsonProperty(nameof(CounterpartyResultType), NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(StringEnumConverter))]
    public CounterpartyResultType? CounterpartyResultType { get; set; }

    [DefaultValue(true)]
    public bool ShouldDisplayInLogs { get; set; } = true;

    public bool ShouldSerializeShouldDisplayInLogs()
    {
        return !ShouldDisplayInLogs;
    }

    [JsonProperty(nameof(Properties))]
    [JsonConverter(typeof(BlockPropertiesNewtonsoftConverter))]
    [CloneIgnore]
    public BlockProperties Properties { get; set; }

    /// <summary>
    /// Regression field bindings: which workflow-level <see cref="WorkflowRegressionField"/>
    /// definitions this block produces and how to locate each value in its output.
    /// Shape: { "fieldName": { "Source": "xml|json|regex|llm", "Locator": "..." } }, parsed via
    /// <c>Testing.RegressionSchemaBindings</c>. Type and tolerance live on the workflow
    /// definition; legacy JSON that still carries them is ignored on read.
    /// </summary>
    [JsonProperty(nameof(RegressionSchema), NullValueHandling = NullValueHandling.Ignore)]
    public string? RegressionSchema { get; set; }

    /// <summary>
    /// Stable identifier minted once when the block is authored. Unlike <see cref="Entity{T}.Id"/> this is
    /// NOT [CloneIgnore], so it survives the template to execution clone and prod to staging transfer, giving
    /// regression testing an exact cross-instance block key and signature overrides a durable one
    /// (paired with the workflow type, since a foreign upload can duplicate stable ids across types).
    /// Null on blocks authored before this field existed until the save-time backfill mints one. Suffixed
    /// deterministically during loop and parameterized expansion so baseline and rerun variants align.
    /// </summary>
    [JsonProperty(nameof(StableBlockId), NullValueHandling = NullValueHandling.Ignore)]
    [StringLength(450)]
    public string? StableBlockId { get; set; }

    /// <summary>
    /// Link record marking this block as a materialized copy of a Library shared component; null on
    /// plain local blocks. Unlike stable ids these fields are copied VERBATIM by every clone and
    /// duplicate path (never re-minted, never suffixed, never backfilled): a duplicate of a live
    /// copy is another live copy. Excluded from the content hash but kept in version payloads. One
    /// component may legitimately appear multiple times in a workflow — never key a dictionary by
    /// this id.
    /// A broken-out copy keeps the id as provenance; <see cref="SharedComponentLinkState"/> is what
    /// distinguishes live from forked.
    /// </summary>
    [JsonProperty(nameof(SharedComponentId), NullValueHandling = NullValueHandling.Ignore)]
    [StringLength(450)]
    [PlaceholderReplacementIgnore]
    public string? SharedComponentId { get; set; }

    [JsonProperty(nameof(SharedComponentLinkState), NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(TolerantStringEnumConverter))]
    public SharedComponentLinkState? SharedComponentLinkState { get; set; }

    /// <summary>
    /// ContentHash of the component version this copy was materialized from: tracks the head while
    /// Linked, freezes as the version-at-fork once BrokenOut.
    /// </summary>
    [JsonProperty(nameof(SharedComponentVersionHash), NullValueHandling = NullValueHandling.Ignore)]
    [StringLength(64)]
    [PlaceholderReplacementIgnore]
    public string? SharedComponentVersionHash { get; set; }

    /// <summary>
    /// Definition slot tag → instance tag map written at materialization. Instance-vs-definition
    /// content comparison is defined as reverse-applying this map, then normalizing, then hashing.
    /// </summary>
    [JsonProperty(nameof(SharedComponentBindings), NullValueHandling = NullValueHandling.Ignore)]
    [PlaceholderReplacementIgnore]
    public Dictionary<string, string>? SharedComponentBindings { get; set; }

    public bool ShouldSerializeSharedComponentBindings()
    {
        return SharedComponentBindings is { Count: > 0 };
    }

    [NotMapped]
    [JsonProperty(nameof(ExecutionState), NullValueHandling = NullValueHandling.Ignore)]
    public BlockExecutionState? ExecutionState { get; set; }

    [NotMapped]
    [JsonIgnore]
    public List<Document> Documents { get; } = [];

    [JsonConstructor]
    internal Block() : this(BlockTypes.AiQuery)
    {
    }

    internal Block(BlockTypes type)
    {
        Type = type;
        Properties = CreateDefaultProperties(type);
    }

    public static BlockProperties CreateDefaultProperties(BlockTypes type)
    {
        return BlockTypeCatalog.Find(type)?.CreateDefaultProperties()
            ?? throw new ArgumentException($"Unsupported block type: {type}");
    }

    public static string ComposeTagWithinLimit(string baseTag, string suffix)
    {
        int room = Math.Max(MAX_REPLACEMENT_TAG_LENGTH - suffix.Length, 0);

        return baseTag.Length <= room
            ? $"{baseTag}{suffix}"
            : $"{baseTag[..room]}{suffix}";
    }

    public Block(BlockTypes type, Section section, string? replacementTag = null) : this(type)
    {
        SectionId = section.Id;
        Section = section;
        section.Add(this);
        ReplacementTag = replacementTag ?? section.MintFreeReplacementTag(DEFAULT_TAG_PREFIX, Order);
        ReMintStableBlockId();
    }

    public Block(BlockTypes type, Section section, int index, string? replacementTag = null) : this(type)
    {
        SectionId = section.Id;
        Section = section;
        section.Insert(index, this);
        ReplacementTag = replacementTag ?? section.MintFreeReplacementTag(DEFAULT_TAG_PREFIX, Order);
        ReMintStableBlockId();
    }

    public Block CloneToSection(Section section, int index = -1, string? replacementTag = null)
    {
        var clone = new Block { Type = Type };
        if (index == -1)
        {
            section.Add(clone);
        }
        else
        {
            section.Insert(index, clone);
        }

        CopyContentTo(clone);
        clone.Section = section;
        clone.SectionId = section.Id;

        clone.ReplacementTag = replacementTag
            ?? (!string.IsNullOrWhiteSpace(ReplacementTag)
                ? ReplacementTag
                : section.MintFreeReplacementTag(DEFAULT_TAG_PREFIX, clone.Order));

        _ = BlockResponse?.CloneToBlock(clone);

        return clone;
    }

    public Block CloneDetached()
    {
        var clone = new Block { Type = Type };

        CopyContentTo(clone);
        clone.Section = null;
        clone.SectionId = null;

        return clone;
    }

    private void CopyContentTo(Block clone)
    {
        CloneHelper.CopyScalars(this, clone);
        clone.Properties = Properties.Clone();

        foreach (TextInput input in TextInputs)
        {
            _ = input.CloneToBlock(clone);
        }

        foreach (RagSettings ragSetting in RagSettings)
        {
            _ = ragSetting.CloneToBlock(clone);
        }
    }

    public void ReplaceAll(
        Dictionary<string, string?> replacements,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        OutputTitleTemplate = OutputTitleTemplate.ReplacePlaceholders(
                asPlaceHolder: false,
                replacements,
                stringComparison)
            ?? OutputTitleTemplate;

        foreach (TextInput blockInput in TextInputs)
        {
            blockInput.ReplaceAll(replacements, stringComparison);
        }

        foreach (RagSettings ragSetting in RagSettings)
        {
            ragSetting.ReplaceAll(replacements, stringComparison);
        }

        Properties.ReplaceAll(replacements, stringComparison);
    }

    public void ApplyReplacementTagRenames(IReadOnlyDictionary<string, string> renames)
    {
        if (renames.Count == 0)
        {
            return;
        }

        if (renames.TryGetValue(ReplacementTag, out string? renamedTag))
        {
            ReplacementTag = renamedTag;
        }

        foreach (TextInput input in TextInputs)
        {
            if (renames.TryGetValue(input.ReplacementTag, out string? renamedInput))
            {
                input.ReplacementTag = renamedInput;
            }
        }

        (Dictionary<string, string?> toTemp, Dictionary<string, string?> toFinal) =
            ReplacementTagRenames.BuildSimultaneousMaps(renames);
        ReplaceAll(toTemp);
        ReplaceAll(toFinal);
    }

    public void UpdateExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState ??= new BlockExecutionState(this);

        ExecutionState.ReplaceAllPlaceholders(
            asPlaceHolder,
            inputs,
            stringComparison);

        foreach (TextInput blockInput in TextInputs)
        {
            blockInput.UpdateExecutionState(
                asPlaceHolder,
                inputs,
                stringComparison);
        }

        foreach (RagSettings ragSetting in RagSettings)
        {
            ragSetting.UpdateExecutionState(
                asPlaceHolder,
                inputs,
                stringComparison);
        }

        Properties.UpdateExecutionState(
            asPlaceHolder,
            inputs,
            stringComparison);
    }

    public void SetExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        // Preserve execution status and timing if execution has started
        ExecutionStatus? previousStatus = ExecutionState?.Status;
        DateTime? previousStartTime = ExecutionState?.StartedAt;
        DateTime? previousCompletedTime = ExecutionState?.CompletedAt;
        string? previousError = ExecutionState?.Error;

        ExecutionState = new BlockExecutionState(this);

        // Restore preserved status and timing if execution has started (Running, Completed, or Failed)
        if (previousStatus.HasValue && previousStatus is not ExecutionStatus.NotExecuted && previousStartTime.HasValue)
        {
            ExecutionState.Status = previousStatus.Value;
            ExecutionState.StartedAt = previousStartTime;
            ExecutionState.CompletedAt = previousCompletedTime;
            ExecutionState.Error = previousError;
        }

        ExecutionState.ReplaceAllPlaceholders(
            asPlaceHolder,
            inputs,
            stringComparison);

        foreach (TextInput blockInput in TextInputs)
        {
            blockInput.SetExecutionState(
                asPlaceHolder,
                inputs,
                stringComparison);
        }

        foreach (RagSettings ragSetting in RagSettings)
        {
            ragSetting.SetExecutionState(
                asPlaceHolder,
                inputs,
                stringComparison);
        }

        Properties.SetExecutionState(
            asPlaceHolder,
            inputs,
            stringComparison);
    }

    public void ClearExecutionState()
    {
        ExecutionState = null;
        BlockResponse = null;
        BlockResponseId = null;
        Properties.ClearExecutionState();

        foreach (TextInput blockInput in TextInputs)
        {
            blockInput.ClearExecutionState();
        }

        foreach (RagSettings ragSetting in RagSettings)
        {
            ragSetting.ClearExecutionState();
        }
    }

    public bool ShouldUseDocuments()
    {
        return Type is BlockTypes.AiQuery
            or BlockTypes.SimpleRag
            or BlockTypes.BuildTopicsForAllDocuments
            || Type.IsAgentFamily();
    }

    /// <summary>
    /// True when this block's failure must abort the execution instead of being absorbed. An Agent
    /// block carries the run's deliverable, so losing it voids the whole upstream spend. A
    /// Verification block defaults to absorbed: machinery failure is not a verdict — the members'
    /// work releases flagged unverified-unchecked instead of the run failing — and the one verdict
    /// that does fail the workflow (unsatisfiable) aborts through its own classified exception,
    /// not through block criticality. Every other type defaults to absorbed.
    /// <see cref="BlockProperties.IsCritical"/> overrides either way.
    /// </summary>
    public bool AbortsExecutionOnFailure()
    {
        return Properties?.IsCritical ?? AbortsExecutionOnFailureByDefault();
    }

    /// <summary>
    /// Whether this block's type aborts the execution on failure when
    /// <see cref="BlockProperties.IsCritical"/> is unset.
    /// </summary>
    public bool AbortsExecutionOnFailureByDefault()
    {
        return Type is BlockTypes.Agent;
    }

    /// <summary>
    /// True when <see cref="SupportedDocumentClasses"/> names concrete classes — as opposed to
    /// the match-everything states (empty, <see cref="DocumentClass.KEY_ANY"/>) and the
    /// deliberately-documentless <see cref="DocumentClass.KEY_NONE"/>.
    /// </summary>
    public bool IsScopedToDocumentClasses()
    {
        return SupportedDocumentClasses.Count > 0
            && !SupportedDocumentClasses.Contains(DocumentClass.KEY_ANY)
            && !SupportedDocumentClasses.Contains(DocumentClass.KEY_NONE);
    }

    /// <summary>
    /// The attached documents this block ships, honoring <see cref="SupportedDocumentClasses"/> at
    /// read time so a class list edited after upload still governs: <see cref="DocumentClass.KEY_NONE"/>
    /// ships nothing, empty/<see cref="DocumentClass.KEY_ANY"/> ship everything, and concrete classes
    /// ship only currently-matching documents.
    /// </summary>
    public List<Document> GetClassScopedDocuments()
    {
        if (!IsScopedToDocumentClasses())
        {
            return SupportedDocumentClasses.Contains(DocumentClass.KEY_NONE) ? [] : [.. Documents];
        }

        return [.. Documents.Where(document =>
            document.CurrentDocumentInfo is { } info && info.CanBeProcessedForBlock(this))];
    }

    /// <summary>
    /// The document class this block's run adds to the workflow's documents, or null when it
    /// creates none.
    /// </summary>
    public string? GetCreatedDocumentClass()
    {
        return Properties is IProducesDocumentBlockProperties producer
            && !string.IsNullOrWhiteSpace(producer.CreatesDocumentClass)
                ? producer.CreatesDocumentClass
                : null;
    }

    /// <summary>
    /// True when a document attaches to this block at run time: a document-using block that is
    /// BuildTopics, agent-family (which ships raw bytes and never queries RAG), or has RAG
    /// settings. Any other block never sees a document, whatever its
    /// <see cref="SupportedDocumentClasses"/> say.
    /// </summary>
    public bool ReceivesDocuments()
    {
        return ShouldUseDocuments()
            && (Type is BlockTypes.BuildTopicsForAllDocuments
                || Type.IsAgentFamily()
                || RagSettings.Count > 0);
    }

    /// <summary>
    /// True when this block reads page overviews: it groups pages by topic, sends overviews to the
    /// model, or retrieves pages with a step that reads them.
    /// </summary>
    public bool ReadsPageOverviews()
    {
        return Type is BlockTypes.BuildTopicsForAllDocuments
            || Properties is AiQueryProperties { ShouldUseOverviews: true }
            || Properties is RetrieveDataFromDocumentsProperties { ShouldUseOverviews: true }
            || (ShouldUseDocuments() && RagSettings.Any(rag => rag.ReadsPageOverviews()));
    }

    /// <summary>
    /// True when this block reads the documents <paramref name="producer"/> creates, so it must run
    /// after it: the block receives documents and names the producer's class exactly in a scoped
    /// <see cref="SupportedDocumentClasses"/>, or it is a Json2Xlsx whose
    /// <see cref="Json2XlsxProperties.InputDocumentClass"/> names it. A block taking every document
    /// (empty or <see cref="DocumentClass.KEY_ANY"/>) or none (<see cref="DocumentClass.KEY_NONE"/>)
    /// depends on no producer: "Any" readers are written for the run's uploaded documents.
    /// </summary>
    public bool ReadsDocumentsCreatedBy(Block producer)
    {
        if (ReferenceEquals(this, producer) || producer.GetCreatedDocumentClass() is not { } documentClass)
        {
            return false;
        }

        return ReadsDocumentClass(documentClass);
    }

    /// <inheritdoc cref="ReadsDocumentsCreatedBy"/>
    public bool ReadsDocumentClass(string documentClass)
    {
        if (Properties is Json2XlsxProperties json2Xlsx
            && string.Equals(json2Xlsx.InputDocumentClass, documentClass, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ReceivesDocuments()
            && IsScopedToDocumentClasses()
            && SupportedDocumentClasses.Contains(documentClass, StringComparer.Ordinal);
    }

    [NotMapped]
    [JsonIgnore]
    public bool IsLoopBoundary => Type is BlockTypes.LoopStart or BlockTypes.LoopEnd;

    public bool HasPlaceholder(
        string placeholder,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        if (string.IsNullOrWhiteSpace(placeholder))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(OutputTitleTemplate)
            && OutputTitleTemplate.Contains(placeholder, stringComparison))
        {
            return true;
        }

        if (Properties.HasPlaceholder(placeholder, stringComparison))
        {
            return true;
        }

        return TextInputs.Any(input => input.HasPlaceholder(placeholder, stringComparison))
            || RagSettings.Any(ragSetting => ragSetting.HasPlaceholder(placeholder, stringComparison));
    }

    public List<string> GetAllPlaceholders()
    {
        return [.. ContentPlaceholders()
            .Concat(RagSettings.SelectMany(ragSetting => ragSetting.GetAllPlaceholders()))
            .Distinct()];
    }

    /// <summary>
    /// The placeholders whose values reach the block's output. RAG settings are left out: their
    /// placeholders only choose which pages the block reads.
    /// </summary>
    private IEnumerable<string> ContentPlaceholders()
    {
        IEnumerable<string> placeholders = Properties.GetAllPlaceholders();

        if (!string.IsNullOrWhiteSpace(OutputTitleTemplate))
        {
            placeholders = placeholders.Concat(OutputTitleTemplate.GetPlaceholders());
        }

        return placeholders.Concat(TextInputs.SelectMany(input => input.GetAllPlaceholders()));
    }

    /// <summary>
    /// Raw template strings from the same sources as <see cref="GetAllPlaceholders"/>, kept in
    /// sync with it. Used by the editor validator so its field coverage matches the runtime's.
    /// </summary>
    public List<string> GetAllTemplateStrings()
    {
        var strings = new List<string>();
        VisitTemplateStrings(text =>
        {
            strings.Add(text);
            return text;
        });
        return strings;
    }

    /// <summary>
    /// Rewrites in place every string <see cref="GetAllTemplateStrings"/> returns, through the same
    /// walk, so a template rewrite cannot miss a field the engine resolves.
    /// </summary>
    public void TransformAllTemplateStrings(Func<string, string> transform)
    {
        VisitTemplateStrings(transform);
        Properties.TransformAgentTemplateTags(transform);
    }

    /// <summary>
    /// <see cref="ReplaceAll"/> for loop expansion: the same replacements also reach the templates
    /// that render under the agent-template grammar, which a rename outside expansion leaves alone.
    /// </summary>
    public void ReplaceAllForExpansion(
        Dictionary<string, string?> replacements,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ReplaceAll(replacements, stringComparison);
        Properties.TransformAgentTemplateTags(text =>
            text.ReplacePlaceholders(asPlaceHolder: false, replacements, stringComparison) ?? text);
    }

    private void VisitTemplateStrings(Func<string, string> visit)
    {
        PlaceholderReflectionHelper.TransformAllTemplateStrings(Properties, visit);

        if (!string.IsNullOrWhiteSpace(OutputTitleTemplate))
        {
            string rewritten = visit(OutputTitleTemplate);
            if (!string.Equals(rewritten, OutputTitleTemplate, StringComparison.Ordinal))
            {
                OutputTitleTemplate = rewritten;
            }
        }

        foreach (TextInput blockInput in TextInputs)
        {
            PlaceholderReflectionHelper.TransformAllTemplateStrings(blockInput, visit);
        }

        foreach (RagSettings ragSetting in RagSettings)
        {
            PlaceholderReflectionHelper.TransformAllTemplateStrings(ragSetting, visit);
        }
    }

    /// <summary>
    /// Gets all unique base replacement tags that this block depends on.
    /// For placeholders like {block1.answer.value}, extracts just "block1".
    /// Handles both single braces {tag} and double braces {{tag}}.
    /// </summary>
    /// <returns>List of unique base replacement tags.</returns>
    public List<string> GetDependencyReplacementTags()
    {
        return BaseTagsOf(GetAllPlaceholders());
    }

    /// <summary>
    /// The base replacement tags whose values reach the block's output: those of its properties,
    /// output title and text inputs, not of its RAG settings, which only choose pages.
    /// </summary>
    public List<string> GetContentDependencyTags()
    {
        return BaseTagsOf(ContentPlaceholders());
    }

    /// <summary>The key placeholders resolve this block by: its replacement tag, else its id.</summary>
    public string GetDependencyKey()
    {
        return string.IsNullOrWhiteSpace(ReplacementTag) ? Id : ReplacementTag;
    }

    private static List<string> BaseTagsOf(IEnumerable<string> placeholders)
    {
        return [.. placeholders
            .Select(placeholder =>
            {
                int dotIndex = placeholder.IndexOf('.');
                return dotIndex >= 0 ? placeholder[..dotIndex] : placeholder;
            })
            .Distinct()];
    }

    public Block CopyToSection(Section section, int index = -1, string? replacementTag = null)
    {
        Block copy = CloneToSection(section, index, replacementTag);
        copy.ReMintStableBlockId();
        return copy;
    }

    [MemberNotNull(nameof(StableBlockId))]
    public void ReMintStableBlockId()
    {
        StableBlockId = Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Grafts a shared component definition's content onto this consumer copy in place: every
    /// shared-content member is copied from <paramref name="definition"/> (Properties, TextInputs,
    /// RagSettings and SupportedDocumentClasses — document-class wiring is shared content) while
    /// the per-consumer fields (Order, ReplacementTag, DataOutputName, and the failure setting
    /// <see cref="BlockProperties.IsCritical"/> inside Properties), the presentation (Name,
    /// OutputTitleTemplate), the instance identity (StableBlockId and the link quartet), parent
    /// wiring and execution artifacts stay untouched.
    /// </summary>
    public void ApplySharedDefinition(Block definition)
    {
        string name = Name;
        string? outputTitleTemplate = OutputTitleTemplate;
        string? dataOutputName = DataOutputName;
        string? stableBlockId = StableBlockId;
        string? sharedComponentId = SharedComponentId;
        SharedComponentLinkState? sharedComponentLinkState = SharedComponentLinkState;
        string? sharedComponentVersionHash = SharedComponentVersionHash;
        Dictionary<string, string>? sharedComponentBindings = SharedComponentBindings;
        BlockExecutionState? executionState = ExecutionState;
        bool? isCritical = Properties.IsCritical;

        CloneHelper.CopyScalars(definition, this);
        Properties = definition.Properties.Clone();
        Properties.IsCritical = isCritical;

        TextInputs.Clear();
        foreach (TextInput input in definition.TextInputs)
        {
            _ = input.CloneToBlock(this);
        }

        RagSettings.Clear();
        foreach (RagSettings ragSetting in definition.RagSettings)
        {
            _ = ragSetting.CloneToBlock(this);
        }

        SupportedDocumentClasses = [.. definition.SupportedDocumentClasses];
        Name = name;
        OutputTitleTemplate = outputTitleTemplate;
        DataOutputName = dataOutputName;
        StableBlockId = stableBlockId;
        SharedComponentId = sharedComponentId;
        SharedComponentLinkState = sharedComponentLinkState;
        SharedComponentVersionHash = sharedComponentVersionHash;
        SharedComponentBindings = sharedComponentBindings;
        ExecutionState = executionState;
    }

    public Block DuplicateBlockToSection(Section section)
    {
        int index = Section?.Id == section.Id
            ? Order + 1
            : -1;

        string copyTag = ComposeTagWithinLimit(ReplacementTag, COPY_TAG_SUFFIX);

        Block duplicate = CopyToSection(section, index, section.FreeReplacementTagFrom(copyTag));

        // Two data outputs under one name would hand the reading system two values for one key.
        duplicate.DataOutputName = null;

        return duplicate;
    }

    // Deterministic stable-id suffix used by loop and parameterized expansion so a baseline variant
    // and a rerun variant align. No-op when there is no stable id (such blocks never match).
    public void SuffixStableBlockId(string suffix)
    {
        if (!string.IsNullOrEmpty(StableBlockId))
        {
            StableBlockId = $"{StableBlockId}_{suffix}";
        }
    }

    // A loop or a parameterized section copies the block once per item or variant; each copy
    // sends its own data output, so it needs its own name.
    public void SuffixDataOutputName(string suffix)
    {
        if (IsDataOutput)
        {
            DataOutputName = ComposeTagWithinLimit(DataOutputName!, $"_{suffix}");
        }
    }

    public void MoveToSection(Section destination, int index = -1)
    {
        if (destination.IsSeparator)
        {
            throw new InvalidOperationException("Cannot move a block into a separator section.");
        }

        if (destination.Id == SectionId && index == Order)
        {
            return;
        }

        index = index == -1 ? destination.Blocks.Count : index;

        if (destination.Id == SectionId)
        {
            Section?.MoveBlock(Order, index);

            return;
        }

        Section? oldParent = Section;
        oldParent?.Remove(this);
        oldParent?.NormalizeBlockOrdering();

        Section = destination;
        SectionId = destination.Id;
        destination.Add(this);
        destination.MoveBlock(Order, index);

        ReplacementTag = destination.FreeReplacementTagFrom(ReplacementTag, this);
    }

    public override string ToString()
    {
        string name = string.IsNullOrWhiteSpace(Name)
            ? Id
            : Name;

        string type = Type switch
        {
            BlockTypes.AiQuery => "AI",
            BlockTypes.Merge => "Merge",
            BlockTypes.CalculateNumber => "Calculation",
            BlockTypes.SimpleRag => "Simple RAG",
            BlockTypes.BuildTopicsForAllDocuments => "Build Topics",
            _ => BlockTypeLabels.Of(Type)
        };

        string replacementTag = string.IsNullOrWhiteSpace(ReplacementTag)
            ? "no replacement tag"
            : ReplacementTag;

        string modelUsed = Type is not BlockTypes.AiQuery || Properties is not AiQueryProperties aiProps
            ? string.Empty
            : !string.IsNullOrWhiteSpace(aiProps.ModelIdOverride)
                ? $", model: {aiProps.ModelIdOverride}"
                : aiProps.ShouldUseAdvancedModel
                    ? ", advanced model"
                    : ", simple model";
        return $"{Order}. {name}, {type}, {replacementTag}{modelUsed}";
    }

    public bool RequiredDocumentsNotProvided()
    {
        // The documents added at runtime might be missing simply because
        // the workflow haven't produced any output documents yet.
        // This is not ideal, but avoids false positives in most cases.
        List<string> runtimeDocumentClasses = Section?.Workflow?
            .DocumentClasses
            .Where(dc => dc.IsOutputDocument)
            .Select(dc => dc.Key)
            .ToList() ?? [];

        if (SupportedDocumentClasses.Any(c => runtimeDocumentClasses.Contains(c)))
        {
            return false;
        }

        bool hasRagSettings = RagSettings.Count > 0;
        bool hasNoDocuments = Documents.Count == 0;
        bool isAiQuery = Type is BlockTypes.AiQuery;
        bool hasSupportedClasses = SupportedDocumentClasses.Count > 0;
        bool isRagMissingDocs = hasRagSettings && hasNoDocuments;
        bool isAiQueryMissingDocs = isAiQuery && hasSupportedClasses && hasNoDocuments && hasRagSettings;

        return isRagMissingDocs || isAiQueryMissingDocs;
    }

    /// <summary>
    /// Resets block state that depends on the current block type. For
    /// document-consuming types (<see cref="ShouldUseDocuments"/>) each
    /// <see cref="RagSettings"/> entry is reset in place. For every
    /// other type, the <see cref="RagSettings"/> and
    /// <see cref="SupportedDocumentClasses"/> collections are cleared
    /// because they are not applicable. All other block fields
    /// (properties, text inputs, name, order, etc.) are left untouched.
    /// </summary>
    public void Reset()
    {
        if (ShouldUseDocuments())
        {
            foreach (RagSettings ragSetting in RagSettings)
            {
                ragSetting.Reset();
            }

            return;
        }

        RagSettings.Clear();
        SupportedDocumentClasses.Clear();
    }
}

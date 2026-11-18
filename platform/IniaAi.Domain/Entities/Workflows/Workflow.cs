using IniaAi.Common;
using IniaAi.Common.Reflection;
using IniaAi.Common.Serialization;
using IniaAi.Domain.Abstractions.Entities;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Citations.Traces;
using IniaAi.Domain.Common;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Numerics;

namespace IniaAi.Domain.Entities.Workflows;

public class Workflow : Entity<Workflow>, ITemplatable
{
    public const int CURRENT_FORMAT_VERSION = 4;

    [NotMapped]
    [JsonProperty(nameof(FormatVersion), NullValueHandling = NullValueHandling.Ignore)]
    public int? FormatVersion { get; set; }

    [JsonProperty(nameof(WorkflowTypeId), NullValueHandling = NullValueHandling.Ignore)]
    public string? WorkflowTypeId { get; set; }

    [JsonIgnore]
    [CloneIgnore]
    public string? UserId { get; set; }

    [JsonIgnore]
    [CloneIgnore]
    public string? OrganizationId { get; set; }

    [JsonIgnore]
    public WorkflowType? WorkflowType { get; set; }

    [JsonIgnore]
    [CloneIgnore]
    public User? User { get; set; }

    [JsonIgnore]
    [CloneIgnore]
    public Organization? Organization { get; set; }

    [JsonProperty(nameof(Sections))]
    [CloneIgnore]
    public ICollection<Section> Sections { get; private set; } = [];

    public bool ShouldSerializeSections()
    {
        return Sections.Count > 0;
    }

    [JsonProperty(nameof(Variables))]
    [CloneIgnore]
    public ICollection<WorkflowVariable> Variables { get; private set; } = [];

    public bool ShouldSerializeVariables()
    {
        return Variables.Count > 0;
    }

    [JsonProperty(nameof(DocumentClasses))]
    [CloneIgnore]
    public ICollection<DocumentClass> DocumentClasses { get; private set; } = [];

    public bool ShouldSerializeDocumentClasses()
    {
        return DocumentClasses.Count > 0;
    }

    [JsonProperty(nameof(RegressionFields))]
    [CloneIgnore]
    public ICollection<WorkflowRegressionField> RegressionFields { get; private set; } = [];

    public bool ShouldSerializeRegressionFields()
    {
        return RegressionFields.Count > 0;
    }

    [JsonProperty(nameof(SignatureFields))]
    [CloneIgnore]
    public ICollection<WorkflowSignatureField> SignatureFields { get; private set; } = [];

    public bool ShouldSerializeSignatureFields()
    {
        return SignatureFields.Count > 0;
    }

    [StringLength(450)]
    [JsonProperty(nameof(Name))]
    public string Name { get; set; } = string.Empty;

    [StringLength(450)]
    [JsonProperty(nameof(Title))]
    public string Title { get; set; } = string.Empty;

    [JsonProperty(nameof(Description))]
    public string? Description { get; set; }

    /// <summary>
    /// What the workflow is for and how it is meant to work, in plain words: the report's purpose
    /// and readers, what each section should produce, conventions, client rules and decisions, and
    /// known weak spots. Intent only, never what the workflow already shows (blocks, settings,
    /// what reads what). Saved with the workflow in the same version. The editor agent reads it
    /// with every message and keeps it current in its proposals; nothing reads it when the
    /// workflow runs.
    /// </summary>
    [JsonProperty(nameof(Notes), NullValueHandling = NullValueHandling.Ignore)]
    public string? Notes { get; set; }

    /// <summary>
    /// Configuration for additional workflow settings.
    /// Supports grid layout, custom settings, and future extensibility.
    /// </summary>
    [JsonProperty(nameof(Configuration), NullValueHandling = NullValueHandling.Ignore)]
    [CloneIgnore]
    public WorkflowConfiguration? Configuration { get; set; }

    public bool ShouldSerializeConfiguration()
    {
        return Configuration is not null;
    }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the workflow-level text input. It can contain
    /// placeholders that get replaced at execution time. Use
    /// <see cref="WorkflowExecutionState.TextInput"/> for the actual text
    /// input after replacements.
    /// </summary>
    [JsonProperty(nameof(TextInputTemplate), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? TextInputTemplate { get; set; }

    [JsonProperty(nameof(ParameterizedConfig), NullValueHandling = NullValueHandling.Ignore)]
    public string? ParameterizedConfig { get; set; }

    /// <summary>
    /// Non-null marks this section as an expanded parameterized variant; the value is the
    /// originating <c>SectionVariant.Key</c>. Such a section must not be saved as core,
    /// which would permanently discard the parameterization.
    /// </summary>
    [JsonProperty(nameof(ExpandedFromParameterizedKey), NullValueHandling = NullValueHandling.Ignore)]
    public string? ExpandedFromParameterizedKey { get; set; }

    [NotMapped]
    [JsonProperty(nameof(ExecutionState), NullValueHandling = NullValueHandling.Ignore)]
    public WorkflowExecutionState? ExecutionState { get; set; }

    /// <summary>
    /// The text of every page the blocks' citation traces point at, filled for a download of an
    /// executed run and read back from one. Never stored with the workflow: the pages are rows of
    /// their own.
    /// </summary>
    [NotMapped]
    [JsonIgnore]
    [CloneIgnore]
    public List<CitationTracePage> CitationTracePages { get; set; } = [];

    /// <summary>The pages the blocks' citation traces point at by span, each once.</summary>
    public List<string> CitationTracePageIds()
    {
        return [.. GetAllBlocks()
            .Select(block => CitationTraceContent.Parse(block.CitationTrace?.Content))
            .OfType<CitationTraceContent>()
            .SelectMany(trace => trace.Sources)
            .Where(source => source.PageId is not null && source.Start is not null)
            .Select(source => source.PageId!)
            .Distinct()];
    }

    /// <summary>
    /// The id each block had in the run its citation traces were recorded in, mapped to its id
    /// here. Traces name blocks by their run ids, and an uploaded run applied to the editor is a
    /// clone with new ids. Empty when the blocks keep their run ids.
    /// </summary>
    [NotMapped]
    [JsonIgnore]
    [CloneIgnore]
    public Dictionary<string, string> TracedBlockIds { get; set; } = [];

    public Workflow()
    {
    }

    public Workflow(string name)
    {
        Name = name;
    }

    private static readonly JsonSerializerSettings _exportWithRunTracesSettings = new()
    {
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
        ContractResolver = new RunTraceExportContractResolver(),
        Converters = { new AgentRunTraceFileExportConverter() },
    };

    internal static JsonSerializerSettings ExportWithRunTracesSettings => _exportWithRunTracesSettings;

    public string ToExportJson(bool includeRunTraces = false)
    {
        int? originalFormatVersion = FormatVersion;
        FormatVersion = CURRENT_FORMAT_VERSION;
        try
        {
            return includeRunTraces
                ? ToJson(pretty: true, _exportWithRunTracesSettings)
                : ToJson(pretty: true);
        }
        finally
        {
            FormatVersion = originalFormatVersion;
        }
    }

    /// <summary>
    /// Deserializes a downloaded <c>.workflow</c> export, restoring the run evidence that plain
    /// <c>FromJson(string?)</c> drops: agent run trace rows, citation traces and the text
    /// of the pages they cite.
    /// </summary>
    public static Workflow? FromExportJson(string? json)
    {
        return json is null ? null : FromJson(json, _exportWithRunTracesSettings);
    }

    /// <summary>
    /// Re-attaches this workflow's agent run trace rows and citation traces onto a clone, matched
    /// by tree position. Cloning deliberately drops evidence rows (<c>[CloneIgnore]</c> — a
    /// duplicated block must not claim another run's evidence), which is wrong for the one
    /// clone that stays the same run: an uploaded export being applied to the editor.
    /// </summary>
    public void CarryRunTracesTo(Workflow clone)
    {
        clone.CitationTracePages = CitationTracePages;
        clone.TracedBlockIds = [];

        IEnumerable<(Section Source, Section Target)> sectionPairs = Sections
            .OrderBy(section => section.Order)
            .Zip(clone.Sections.OrderBy(section => section.Order));
        foreach ((Section source, Section target) in sectionPairs)
        {
            IEnumerable<(Block Source, Block Target)> blockPairs = source.Blocks
                .OrderBy(block => block.Order)
                .Zip(target.Blocks.OrderBy(block => block.Order));
            foreach ((Block sourceBlock, Block targetBlock) in blockPairs)
            {
                clone.TracedBlockIds[sourceBlock.Id] = targetBlock.Id;

                foreach (AgentRunTraceFile file in sourceBlock.AgentRunTraceFiles)
                {
                    file.BlockId = targetBlock.Id;
                    file.Block = null;
                    targetBlock.AgentRunTraceFiles.Add(file);
                }

                sourceBlock.AgentRunTraceFiles.Clear();

                if (sourceBlock.CitationTrace is BlockCitationTrace citationTrace)
                {
                    citationTrace.BlockId = targetBlock.Id;
                    citationTrace.Block = null;
                    targetBlock.CitationTrace = citationTrace;
                    sourceBlock.CitationTrace = null;
                }
            }
        }
    }

    public static bool DeclaresUnsupportedFormatVersion(string json, out int declaredFormatVersion)
    {
        declaredFormatVersion = 0;
        try
        {
            return JToken.Parse(SanitizeJson(json)) is JObject root
                && DeclaresUnsupportedFormatVersion(root, out declaredFormatVersion);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool DeclaresUnsupportedFormatVersion(JObject root, out int declaredFormatVersion)
    {
        declaredFormatVersion = 0;
        if (root[nameof(FormatVersion)] is not JValue { Type: JTokenType.Integer } declared)
        {
            return false;
        }

        declaredFormatVersion = declared.Value switch
        {
            BigInteger bigValue when bigValue > int.MaxValue => int.MaxValue,
            BigInteger bigValue when bigValue < int.MinValue => int.MinValue,
            BigInteger bigValue => (int)bigValue,
            long longValue => (int)Math.Clamp(longValue, int.MinValue, int.MaxValue),
            _ => (int)declared,
        };

        return DeclaresUnsupportedFormatVersion(declaredFormatVersion);
    }

    public static bool DeclaresUnsupportedFormatVersion(int? declaredFormatVersion)
    {
        return declaredFormatVersion > CURRENT_FORMAT_VERSION;
    }

    public static string UnsupportedFormatVersionMessage(int declaredFormatVersion)
    {
        return $"This workflow file uses format version {declaredFormatVersion}, but this platform "
            + $"supports up to version {CURRENT_FORMAT_VERSION}. Update the platform before importing this file.";
    }

    public void Clear()
    {
        Sections.Clear();
        Variables.Clear();
        DocumentClasses.Clear();
        RegressionFields.Clear();
        SignatureFields.Clear();
    }

    public void CloneTo(Workflow destination)
    {
        destination.Clear();

        CloneHelper.CopyScalars(this, destination);

        destination.Configuration = Configuration?.Clone();

        foreach (WorkflowVariable variable in Variables)
        {
            _ = variable.CloneToWorkflow(destination);
        }

        foreach (DocumentClass documentClass in DocumentClasses)
        {
            _ = documentClass.CloneToWorkflow(destination);
        }

        foreach (WorkflowRegressionField regressionField in RegressionFields)
        {
            _ = regressionField.CloneToWorkflow(destination);
        }

        foreach (WorkflowSignatureField signatureField in SignatureFields)
        {
            _ = signatureField.CloneToWorkflow(destination);
        }

        foreach (Section section in Sections.OrderBy(s => s.Order))
        {
            var destinationSection = new Section(section.Name, destination);
            section.CloneTo(destinationSection);
        }
    }

    /// <summary>
    /// Clones like <see cref="CloneTo"/>, then overwrites every entity Id in the
    /// destination graph with the matching Id from the source. Lets round-trips
    /// like editor-load → edit → save re-land on the same DB rows they came from.
    /// </summary>
    public void CloneToPreservingIds(Workflow destination)
    {
        CloneTo(destination);
        destination.Id = Id;
        OverwriteChildIds(this, destination);

        // OverwriteChildIds rewrites parent ids (Workflow.Id, Section.Id) but
        // child entities like Block, WorkflowVariable, DocumentClass keep the
        // transient WorkflowId/SectionId values that CloneTo set against the
        // freshly-generated parent GUIDs. Those parent GUIDs no longer exist
        // anywhere in the tree, so the FK columns are dangling. Re-sync every
        // nav FK to the now-correct parent Id before handing the clone back.
        FixNavigationProperties(destination);
    }

    private static void OverwriteChildIds(Workflow source, Workflow destination)
    {
        List<WorkflowVariable> srcVars = [.. source.Variables];
        List<WorkflowVariable> dstVars = [.. destination.Variables];
        for (int i = 0; i < srcVars.Count && i < dstVars.Count; i++)
        {
            dstVars[i].Id = srcVars[i].Id;
        }

        List<DocumentClass> srcDocs = [.. source.DocumentClasses];
        List<DocumentClass> dstDocs = [.. destination.DocumentClasses];
        for (int i = 0; i < srcDocs.Count && i < dstDocs.Count; i++)
        {
            dstDocs[i].Id = srcDocs[i].Id;
        }

        List<WorkflowRegressionField> srcFields = [.. source.RegressionFields];
        List<WorkflowRegressionField> dstFields = [.. destination.RegressionFields];
        for (int i = 0; i < srcFields.Count && i < dstFields.Count; i++)
        {
            dstFields[i].Id = srcFields[i].Id;
        }

        List<WorkflowSignatureField> srcSigFields = [.. source.SignatureFields];
        List<WorkflowSignatureField> dstSigFields = [.. destination.SignatureFields];
        for (int i = 0; i < srcSigFields.Count && i < dstSigFields.Count; i++)
        {
            dstSigFields[i].Id = srcSigFields[i].Id;
        }

        List<Section> srcSections = [.. source.Sections.OrderBy(s => s.Order)];
        List<Section> dstSections = [.. destination.Sections.OrderBy(s => s.Order)];
        for (int i = 0; i < srcSections.Count && i < dstSections.Count; i++)
        {
            dstSections[i].Id = srcSections[i].Id;
            OverwriteBlockIds(srcSections[i].Blocks, dstSections[i].Blocks);
        }
    }

    private static void OverwriteBlockIds(
        ICollection<Block> sourceBlocks,
        ICollection<Block> destinationBlocks)
    {
        List<Block> srcBlocks = [.. sourceBlocks.OrderBy(b => b.Order)];
        List<Block> dstBlocks = [.. destinationBlocks.OrderBy(b => b.Order)];
        for (int i = 0; i < srcBlocks.Count && i < dstBlocks.Count; i++)
        {
            Block src = srcBlocks[i];
            Block dst = dstBlocks[i];
            dst.Id = src.Id;

            if (src.BlockResponse is not null && dst.BlockResponse is not null)
            {
                dst.BlockResponse.Id = src.BlockResponse.Id;
            }

            List<TextInput> srcInputs = [.. src.TextInputs];
            List<TextInput> dstInputs = [.. dst.TextInputs];
            for (int j = 0; j < srcInputs.Count && j < dstInputs.Count; j++)
            {
                dstInputs[j].Id = srcInputs[j].Id;
            }

            List<RagSettings> srcRag = [.. src.RagSettings];
            List<RagSettings> dstRag = [.. dst.RagSettings];
            for (int j = 0; j < srcRag.Count && j < dstRag.Count; j++)
            {
                dstRag[j].Id = srcRag[j].Id;
            }
        }
    }

    public List<string> GetDocumentClasses()
    {
        return DocumentClassKeysOf(DocumentClasses);
    }

    public static List<string> DocumentClassKeysOf(IEnumerable<DocumentClass> documentClasses)
    {
        return [.. documentClasses
            .Select(dc => dc.Key)
            .Distinct()
            .Except([DocumentClass.KEY_ANY, DocumentClass.KEY_NONE])
            .Order()];
    }

    public List<string> GetInputDocumentClasses()
    {
        return [.. DocumentClasses
            .Where(dc => !dc.IsOutputDocument)
            .Select(dc => dc.Key)
            .Distinct()
            .Except([DocumentClass.KEY_ANY, DocumentClass.KEY_NONE])
            .Order()];
    }

    public void RenameDocumentClassKey(DocumentClass documentClass, string newKey)
    {
        string oldKey = documentClass.Key;
        documentClass.Key = newKey;

        if (string.IsNullOrWhiteSpace(oldKey) || oldKey == newKey)
        {
            return;
        }

        foreach (Block block in GetAllBlocks())
        {
            RenameBlockDocumentClassReferences(block, oldKey, newKey);
        }

        foreach (Section section in Sections)
        {
            RenameSectionVariantDocumentClassReferences(section, oldKey, newKey);
        }

        foreach (WorkflowVariable variable in Variables)
        {
            variable.RenameInputRuleDocumentClass(oldKey, newKey);
        }
    }

    public void RemoveDocumentClass(DocumentClass documentClass)
    {
        DocumentClasses.Remove(documentClass);

        string key = documentClass.Key;
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        foreach (Block block in GetAllBlocks())
        {
            RemoveBlockDocumentClassReferences(block, key);
        }

        foreach (Section section in Sections)
        {
            RemoveSectionVariantDocumentClassReferences(section, key);
        }
    }

    private static void RenameBlockDocumentClassReferences(Block block, string oldKey, string newKey)
    {
        bool replaced = false;
        for (int i = 0; i < block.SupportedDocumentClasses.Count; i++)
        {
            if (block.SupportedDocumentClasses[i] == oldKey)
            {
                block.SupportedDocumentClasses[i] = newKey;
                replaced = true;
            }
        }

        if (replaced)
        {
            DistinctInPlace(block.SupportedDocumentClasses);
        }

        if (block.Properties is IProducesDocumentBlockProperties producer
            && producer.CreatesDocumentClass == oldKey)
        {
            producer.CreatesDocumentClass = newKey;
        }

        if (block.Properties is Json2XlsxProperties json2Xlsx
            && json2Xlsx.InputDocumentClass == oldKey)
        {
            json2Xlsx.InputDocumentClass = newKey;
        }
    }

    private static void RemoveBlockDocumentClassReferences(Block block, string key)
    {
        int removed = block.SupportedDocumentClasses.RemoveAll(c => c == key);
        if (removed > 0 && block.SupportedDocumentClasses.Count == 0)
        {
            // Inside a parameterized section the block's classes are the variant maps' pivot:
            // falling back to "Any" would silently feed every variant all documents, so the
            // block starves instead. Plain sections keep the historical "Any" fallback.
            bool isParameterized = !string.IsNullOrWhiteSpace(block.Section?.ParameterizedConfig);
            block.SupportedDocumentClasses.Add(isParameterized ? DocumentClass.KEY_NONE : DocumentClass.KEY_ANY);
        }

        if (block.Properties is IProducesDocumentBlockProperties producer
            && producer.CreatesDocumentClass == key)
        {
            producer.CreatesDocumentClass = null;
        }

        if (block.Properties is Json2XlsxProperties json2Xlsx
            && json2Xlsx.InputDocumentClass == key)
        {
            json2Xlsx.InputDocumentClass = null;
        }
    }

    private static void RenameSectionVariantDocumentClassReferences(Section section, string oldKey, string newKey)
    {
        UpdateParameterizedVariantClasses(
            section,
            classes =>
            {
                bool changed = false;
                for (int i = 0; i < classes.Count; i++)
                {
                    if (classes[i] == oldKey)
                    {
                        classes[i] = newKey;
                        changed = true;
                    }
                }

                if (changed)
                {
                    DistinctInPlace(classes);
                }

                return changed;
            },
            map =>
            {
                if (!map.ContainsKey(oldKey) && !map.Values.Any(targets => targets.Contains(oldKey)))
                {
                    return null;
                }

                var rewritten = new Dictionary<string, List<string>>();
                foreach ((string key, List<string> targets) in map)
                {
                    if (key != oldKey)
                    {
                        rewritten[key] = [.. targets.Select(t => t == oldKey ? newKey : t).Distinct()];
                    }
                }

                // On a key collision the pre-existing entry wins, regardless of serialized order.
                if (map.TryGetValue(oldKey, out List<string>? renamedTargets))
                {
                    _ = rewritten.TryAdd(newKey, [.. renamedTargets.Select(t => t == oldKey ? newKey : t).Distinct()]);
                }

                return rewritten;
            });
    }

    private static void RemoveSectionVariantDocumentClassReferences(Section section, string key)
    {
        UpdateParameterizedVariantClasses(
            section,
            classes =>
            {
                if (classes.RemoveAll(c => c == key) == 0)
                {
                    return false;
                }

                if (classes.Count == 0)
                {
                    classes.Add(DocumentClass.KEY_ANY);
                }

                return true;
            },
            map =>
            {
                bool changed = false;
                var rewritten = new Dictionary<string, List<string>>();
                foreach ((string entryKey, List<string> targets) in map)
                {
                    if (entryKey == key)
                    {
                        changed = true;
                        continue;
                    }

                    List<string> kept = [.. targets.Where(t => t != key)];
                    if (kept.Count == targets.Count)
                    {
                        rewritten[entryKey] = targets;
                        continue;
                    }

                    // Emptied target list becomes "None": starve the blocks rather than
                    // silently feed them the source class's documents.
                    rewritten[entryKey] = kept.Count > 0 ? kept : [DocumentClass.KEY_NONE];
                    changed = true;
                }

                return changed ? rewritten : null;
            });
    }

    private static void DistinctInPlace(List<string> values)
    {
        List<string> distinct = [.. values.Distinct()];
        if (distinct.Count != values.Count)
        {
            values.Clear();
            values.AddRange(distinct);
        }
    }

    private static void UpdateParameterizedVariantClasses(
        Section section,
        Func<List<string>, bool> mutate,
        Func<Dictionary<string, List<string>>, Dictionary<string, List<string>>?>? rewriteClassMap = null)
    {
        if (string.IsNullOrWhiteSpace(section.ParameterizedConfig))
        {
            return;
        }

        ParameterizedSectionConfig? config;
        try
        {
            config = JsonConvert.DeserializeObject<ParameterizedSectionConfig>(section.ParameterizedConfig);
        }
        catch (JsonException)
        {
            return;
        }

        if (config?.Variants is null)
        {
            return;
        }

        bool changed = false;
        for (int i = 0; i < config.Variants.Count; i++)
        {
            SectionVariant variant = config.Variants[i];
            if (variant.SectionSupportedDocumentClasses is { } classes)
            {
                changed |= mutate(classes);
            }

            if (rewriteClassMap is not null && variant.ClassMap is { } map)
            {
                Dictionary<string, List<string>>? rewritten = rewriteClassMap(map);
                if (rewritten is not null)
                {
                    config.Variants[i] = variant with { ClassMap = rewritten };
                    changed = true;
                }
            }
        }

        if (changed)
        {
            section.ParameterizedConfig = JsonConvert.SerializeObject(config);
        }
    }

    public string? GetBlockOutput(string blockPointer)
    {
        string[] parts = blockPointer.Split('.');

        List<Block> allBlocks = GetAllBlocks();

        Block? block = allBlocks.FirstOrDefault(q => q.ReplacementTag == parts[0]);

        return block is null
            ? null
            : parts.Length == 1
            ? (block.BlockResponse?.RawResponse)
            : (block.BlockResponse?.GetResponseProperty(parts[1]));
    }

    public List<Block> GetAllBlocks()
    {
        return [.. Sections.SelectMany(s => s.Blocks)];
    }

    /// <summary>
    /// Every block whose properties implement <see cref="IHasSignatureFields"/>. Override rows
    /// are keyed by field name, so no per-block id is required.
    /// </summary>
    public IEnumerable<Block> GetSignatureBearingBlocks()
    {
        return GetAllBlocks().Where(q => q.Properties is IHasSignatureFields);
    }

    /// <summary>
    /// The derived binding between workflow-level signature field definitions and blocks: a block
    /// references a field iff any of its templated strings contains the <c>{Name}</c> token
    /// (case-insensitive) — the exact surface runtime placeholder substitution operates on
    /// (<see cref="Blocks.Properties.Abstractions.BlockProperties.HasPlaceholder"/>). Returns every
    /// definition name (keyed case-insensitively) mapped to the referencing bearing blocks in
    /// (section order, block order); unreferenced definitions map to an empty list.
    /// </summary>
    public Dictionary<string, List<Block>> GetSignatureFieldReferences()
    {
        var references = new Dictionary<string, List<Block>>(StringComparer.OrdinalIgnoreCase);
        foreach (WorkflowSignatureField field in SignatureFields)
        {
            references[field.Name] = [];
        }

        if (references.Count == 0)
        {
            return references;
        }

        IEnumerable<Block> orderedBearers = Sections
            .OrderBy(s => s.Order)
            .SelectMany(s => s.Blocks.OrderBy(b => b.Order))
            .Where(b => b.Properties is IHasSignatureFields);

        foreach (Block block in orderedBearers)
        {
            foreach (WorkflowSignatureField field in SignatureFields)
            {
                if (block.Properties.HasPlaceholder(
                        SignatureFieldToken(field.Name), StringComparison.OrdinalIgnoreCase))
                {
                    references[field.Name].Add(block);
                }
            }
        }

        return references;
    }

    /// <summary>The template token form of a signature field name: <c>{name}</c>.</summary>
    public static string SignatureFieldToken(string name)
    {
        return "{" + name + "}";
    }

    /// <summary>
    /// Recursively collects the titles of every descendant section that was produced by
    /// expanding a parameterized template (i.e. carries <see cref="ExpandedFromParameterizedKey"/>).
    /// </summary>
    public IReadOnlyList<string> GetExpandedParameterizedSectionTitles()
    {
        var titles = new List<string>();
        foreach (Section section in Sections)
        {
            if (!string.IsNullOrWhiteSpace(section.ExpandedFromParameterizedKey))
            {
                titles.Add(section.Title);
            }
        }

        return titles;
    }

    public Block? FindBlockById(string id)
    {
        return GetAllBlocks()?.FirstOrDefault(q => q.Id == id);
    }

    public Block? FindBlockByStableId(string stableBlockId)
    {
        return GetAllBlocks()?.FirstOrDefault(q => q.StableBlockId == stableBlockId);
    }

    /// <summary>
    /// Takes over the stable block/section ids of <paramref name="source"/>, the workflow row this
    /// instance is about to replace on save. Identity-first: an incoming id that already exists in
    /// the source row and is carried by exactly ONE incoming entity IS the identity and is kept
    /// untouched — a client that carried real ids through a section move or reorder must not have
    /// them reassigned by position. Everything else resolves structurally (sections by <c>Order</c>,
    /// blocks by section <c>Order</c> + <c>ReplacementTag</c> + <c>Type</c>, only where the key is
    /// unique on both sides): rebuilt objects and id-less legacy payloads arrive with fresh
    /// ctor-minted ids for what is semantically the same block, and adopting the established
    /// identity is what keeps regression baselines and replay captures aligned across saves. A
    /// CONTESTED id (the same known id carried by several incoming entities, e.g. a block's JSON
    /// copied verbatim into another section) is granted only by structural corroboration: each
    /// contestant takes whatever the structural match assigns it, and a contestant with no match is
    /// re-minted rather than allowed to keep — or steal by document order — an identity that
    /// structure says belongs elsewhere. A structural match likewise never assigns an id a kept
    /// entity already claims. Unmatched entities keep what they carried; the save's backfill mints
    /// ids for any left null.
    /// </summary>
    public void AdoptStableIdsFrom(Workflow source)
    {
        var sourceSectionIds = new HashSet<string>(
            source.Sections
                .Select(s => s.StableSectionId)
                .Where(id => !string.IsNullOrEmpty(id))!,
            StringComparer.Ordinal);
        HashSet<string> keptSectionIds = UncontestedKnownIds(
            Sections.Select(s => s.StableSectionId), sourceSectionIds);

        Dictionary<int, Section> sourceSections = UniquelyKeyed(source.Sections, s => s.Order);
        Dictionary<int, Section> ownSections = UniquelyKeyed(Sections, s => s.Order);
        foreach ((int order, Section section) in ownSections)
        {
            if (!string.IsNullOrEmpty(section.StableSectionId)
                && keptSectionIds.Contains(section.StableSectionId))
            {
                continue;
            }

            bool contested = !string.IsNullOrEmpty(section.StableSectionId)
                && sourceSectionIds.Contains(section.StableSectionId);
            if (sourceSections.TryGetValue(order, out Section? sourceSection)
                && !string.IsNullOrEmpty(sourceSection.StableSectionId)
                && !keptSectionIds.Contains(sourceSection.StableSectionId))
            {
                section.StableSectionId = sourceSection.StableSectionId;
            }
            else if (contested)
            {
                // Lost the contest for a duplicated known id: re-mint via the save's backfill
                // instead of letting document order decide who keeps the identity.
                section.StableSectionId = null;
            }
        }

        var sourceBlockIds = new HashSet<string>(
            source.GetAllBlocks()
                .Select(b => b.StableBlockId)
                .Where(id => !string.IsNullOrEmpty(id))!,
            StringComparer.Ordinal);
        HashSet<string> keptBlockIds = UncontestedKnownIds(
            GetAllBlocks().Select(b => b.StableBlockId), sourceBlockIds);

        var sourceBlocks = UniquelyKeyed(
            source.Sections.SelectMany(s => s.Blocks.Select(b => (Section: s, Block: b))),
            e => (e.Section.Order, e.Block.ReplacementTag, e.Block.Type));
        var ownBlocks = UniquelyKeyed(
            Sections.SelectMany(s => s.Blocks.Select(b => (Section: s, Block: b))),
            e => (e.Section.Order, e.Block.ReplacementTag, e.Block.Type));
        foreach ((var key, (Section _, Block block)) in ownBlocks)
        {
            if (!string.IsNullOrEmpty(block.StableBlockId)
                && keptBlockIds.Contains(block.StableBlockId))
            {
                continue;
            }

            bool contested = !string.IsNullOrEmpty(block.StableBlockId)
                && sourceBlockIds.Contains(block.StableBlockId);
            if (sourceBlocks.TryGetValue(key, out (Section Section, Block Block) sourceEntry)
                && !string.IsNullOrEmpty(sourceEntry.Block.StableBlockId)
                && !keptBlockIds.Contains(sourceEntry.Block.StableBlockId))
            {
                block.StableBlockId = sourceEntry.Block.StableBlockId;
            }
            else if (contested)
            {
                block.StableBlockId = null;
            }
        }
    }

    /// <summary>
    /// Ids that exist in <paramref name="knownIds"/> and are carried by exactly one incoming
    /// entity. Only these are trusted as identity; a duplicated known id is contested and must be
    /// resolved structurally.
    /// </summary>
    private static HashSet<string> UncontestedKnownIds(
        IEnumerable<string?> incomingIds,
        HashSet<string> knownIds)
    {
        return incomingIds
            .Where(id => !string.IsNullOrEmpty(id) && knownIds.Contains(id!))
            .GroupBy(id => id!, StringComparer.Ordinal)
            .Where(g => g.Count() == 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static Dictionary<TKey, TItem> UniquelyKeyed<TKey, TItem>(
        IEnumerable<TItem> items,
        Func<TItem, TKey> key)
        where TKey : notnull
    {
        return items
            .GroupBy(key)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single());
    }

    public Block? FindBlockByReplacementTag(string replacementTag)
    {
        return GetAllBlocks()?.FirstOrDefault(q => q.ReplacementTag == replacementTag);
    }

    public IEnumerable<Block> FindBlocksByReplacementTag(string replacementTag)
    {
        return GetAllBlocks()?.Where(q => q.ReplacementTag == replacementTag)
            ?? [];
    }

    public bool ReplacementTagIsInUse(string replacementTag)
    {
        return Sections.Any(s => s.ReplacementTagIsInUse(replacementTag));
    }

    public static new Workflow? FromJson(string json, JsonSerializerSettings? settings = null)
    {
        Workflow? workflow = JsonSerializable<Workflow>.FromJson(json, settings);

        if (workflow is null)
        {
            return null;
        }

        workflow.NormalizeSectionOrdering();

        foreach (Section section in workflow.Sections)
        {
            section.NormalizeBlockOrdering();
        }

        FixNavigationProperties(workflow);

        RegressionSchemaLegacyMigrator.Hoist(workflow);
        IniaAi.Domain.Signatures.SignatureFieldsLegacyMigrator.Hoist(workflow);

        return workflow;
    }

    public void ClearExecutionState()
    {
        ExecutionState = null;

        foreach (Block block in GetAllBlocks())
        {
            block.ClearExecutionState();
        }
    }

    public bool RequiredDocumentsNotProvided()
    {
        return Sections.Any(s => s.RequiredDocumentsNotProvided());
    }

    public void ReplaceAll(
        Dictionary<string, string?> replacements,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        PlaceholderReflectionHelper.ReplaceAll(this, false, replacements, stringComparison);

        foreach (Section section in Sections)
        {
            section.ReplaceAll(replacements, stringComparison);
        }
    }

    public bool HasPlaceholder(
        string placeholder,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        if (PlaceholderReflectionHelper.HasPlaceholder(this, placeholder, stringComparison))
        {
            return true;
        }

        return Sections.Any(s => s.HasPlaceholder(placeholder, stringComparison));
    }

    public List<string> GetAllPlaceholders()
    {
        var placeholders = new List<string>();

        if (!string.IsNullOrWhiteSpace(TextInputTemplate))
        {
            placeholders.AddRange(TextInputTemplate.GetPlaceholders());
        }

        return [.. placeholders.Distinct()];
    }

    public void ConvertToDemoWorkflow()
    {
        List<Block> allBlocks = GetAllBlocks();

        if (allBlocks.All(b => b.BlockResponse is null))
        {
            throw new InvalidOperationException(
                "Cannot convert to demo workflow as no block responses are available.");
        }

        foreach (Block block in allBlocks)
        {
            if (block.Type is BlockTypes.DynamicChart)
            {
                ConvertDynamicChartToDemo(block);

                continue;
            }

            string? response = block.BlockResponse?.RawResponse;

            block.Type = BlockTypes.Merge;
            block.Properties = new MergeProperties { TextTemplate = response };
            block.RagSettings.Clear();

            foreach (TextInput textInput in block.TextInputs)
            {
                textInput.Value = textInput.ExecutionState?.Value ?? textInput.Value;
            }

            block.OutputTitleTemplate = block.ExecutionState?.OutputTitle ?? block.OutputTitleTemplate;
        }
    }

    public void ExposeAllBlocksAsOutputs()
    {
        foreach (Block block in GetAllBlocks())
        {
            block.IsOutputSource = true;
            block.OutputTitleTemplate = !string.IsNullOrWhiteSpace(block.OutputTitleTemplate)
                ? block.OutputTitleTemplate
                : !string.IsNullOrWhiteSpace(block.Name)
                    ? block.Name
                    : block.ReplacementTag;
        }
    }

    /// <summary>
    /// The blocks that <see cref="SetEveryBlockToStopTheRun"/> changes: each block not yet set to
    /// stop the run. Loop boundaries and blocks whose settings this build keeps as stored are left
    /// out: the editor offers them no failure setting.
    /// </summary>
    public List<Block> GetBlocksNotSetToStopTheRun()
    {
        return [.. GetAllBlocks().Where(block => !block.IsLoopBoundary
            && block.Properties is not UnavailableBlockProperties
            && block.Properties.IsCritical is not true)];
    }

    /// <summary>
    /// Sets each block that <see cref="GetBlocksNotSetToStopTheRun"/> returns to stop the run when
    /// it fails.
    /// </summary>
    public void SetEveryBlockToStopTheRun()
    {
        foreach (Block block in GetBlocksNotSetToStopTheRun())
        {
            block.Properties.IsCritical = true;
        }
    }

    internal void Add(Section section)
    {
        section.Order = Sections.Count;
        section.Workflow = this;
        section.WorkflowId = Id;
        Sections.Add(section);
    }

    public void Remove(Section section)
    {
        if (!Sections.Remove(section))
        {
            return;
        }

        NormalizeSectionOrdering();
    }

    public Section RemoveSectionAt(int index)
    {
        Section section = Sections.FirstOrDefault(s => s.Order == index)
            ?? throw new IndexOutOfRangeException($"Section at index {index} not found.");

        Remove(section);

        return section;
    }

    public void NormalizeSectionOrdering()
    {
        var ordered = Sections.OrderBy(s => s.Order).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            ordered[i].Order = i;
        }

        Sections.Clear();
        foreach (Section s in ordered)
        {
            Sections.Add(s);
        }
    }

    public void ReplaceSectionWithExpanded(Section templateSection, List<Section> expandedSections)
    {
        int insertIndex = templateSection.Order;

        Remove(templateSection);

        foreach (Section expanded in expandedSections)
        {
            expanded.Workflow = this;
            expanded.WorkflowId = Id;
            Insert(insertIndex, expanded);

            insertIndex++;
        }

        NormalizeSectionOrdering();
    }

    internal void MoveSection(int fromIndex, int toIndex)
    {
        var ordered = Sections.OrderBy(s => s.Order).ToList();
        if (fromIndex < 0 || fromIndex >= ordered.Count)
        {
            throw new IndexOutOfRangeException("From index out of range.");
        }

        Section section = ordered[fromIndex];
        ordered.RemoveAt(fromIndex);

        if (toIndex < 0)
        {
            toIndex = 0;
        }
        else if (toIndex > ordered.Count)
        {
            toIndex = ordered.Count;
        }

        ordered.Insert(toIndex, section);

        for (int i = 0; i < ordered.Count; i++)
        {
            ordered[i].Order = i;
        }

        Sections.Clear();
        foreach (Section s in ordered)
        {
            Sections.Add(s);
        }
    }

    public Section DuplicateSectionAt(int index)
    {
        Section toDuplicate = Sections.FirstOrDefault(s => s.Order == index)
            ?? throw new IndexOutOfRangeException($"Section at index {index} not found.");

        if (toDuplicate.IsSeparator)
        {
            throw new InvalidOperationException(
                "Cannot duplicate a separator. Add a new separator instead.");
        }

        var duplicated = new Section($"{toDuplicate.Name} - Copy", this, index + 1);
        toDuplicate.CloneTo(duplicated);
        duplicated.ReMintStableIds();

        duplicated.Name = $"{toDuplicate.Name} - Copy";
        duplicated.Workflow = this;
        duplicated.WorkflowId = Id;
        duplicated.Order = index + 1;

        return duplicated;
    }

    internal void Insert(int index, Section section)
    {
        var ordered = Sections.OrderBy(s => s.Order).ToList();
        int clamped = Math.Clamp(index, 0, ordered.Count);
        ordered.Insert(clamped, section);
        section.Workflow = this;
        section.WorkflowId = Id;

        for (int i = 0; i < ordered.Count; i++)
        {
            ordered[i].Order = i;
        }

        Sections.Clear();
        foreach (Section s in ordered)
        {
            Sections.Add(s);
        }
    }

    public override string ToString()
    {
        return $"Root: {Name}, {WorkflowTypeId}";
    }

    private static void ConvertDynamicChartToDemo(Block block)
    {
        foreach (TextInput input in block.TextInputs)
        {
            input.Value = input.ExecutionState?.Value ?? input.Value;
        }

        block.OutputTitleTemplate = block.ExecutionState?.OutputTitle ?? block.OutputTitleTemplate;
    }

    internal static void FixNavigationProperties(Workflow workflow)
    {
        foreach (Section section in workflow.Sections)
        {
            section.Workflow = workflow;
            section.WorkflowId = workflow.Id;

            foreach (Block block in section.Blocks)
            {
                block.Section = section;
                block.SectionId = section.Id;
                FixBlockChildIds(block);
            }
        }

        foreach (WorkflowVariable variable in workflow.Variables)
        {
            variable.Workflow = workflow;
            variable.WorkflowId = workflow.Id;
        }

        foreach (DocumentClass documentClass in workflow.DocumentClasses)
        {
            documentClass.Workflow = workflow;
            documentClass.WorkflowId = workflow.Id;
        }

        foreach (WorkflowRegressionField regressionField in workflow.RegressionFields)
        {
            regressionField.Workflow = workflow;
            regressionField.WorkflowId = workflow.Id;
        }

        foreach (WorkflowSignatureField signatureField in workflow.SignatureFields)
        {
            signatureField.Workflow = workflow;
            signatureField.WorkflowId = workflow.Id;
        }
    }

    internal static void FixBlockChildIds(Block block)
    {
        if (block.BlockResponse is not null)
        {
            block.BlockResponse.Block = block;
            block.BlockResponse.BlockId = block.Id;
            block.BlockResponseId = block.BlockResponse.Id;
        }
        else
        {
            block.BlockResponseId = null;
        }

        foreach (RagSettings ragSettings in block.RagSettings)
        {
            ragSettings.Block = block;
            ragSettings.BlockId = block.Id;
        }

        foreach (TextInput textInput in block.TextInputs)
        {
            textInput.Block = block;
            textInput.BlockId = block.Id;
        }
    }

    public static bool GetAvailability(string? workflowType)
    {
        return workflowType switch
        {
            WorkflowType.TEST_WORKFLOW_TYPE => false,
            WorkflowType.GENERIC => false,
            _ => true
        };
    }

    /// <summary>
    /// Updates or initializes the WorkflowExecutionState.
    /// </summary>
    public void UpdateExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState ??= new WorkflowExecutionState(this);

        PlaceholderReflectionHelper.ReplaceAll(ExecutionState, asPlaceHolder, inputs, stringComparison);
    }

    /// <summary>
    /// Resets and sets the WorkflowExecutionState.
    /// </summary>
    public void SetExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState = new WorkflowExecutionState(this);

        PlaceholderReflectionHelper.ReplaceAll(ExecutionState, asPlaceHolder, inputs, stringComparison);
    }

    /// <summary>
    /// Isolates a block by keeping only the target block and all its dependencies (including transitive).
    /// Removes all other blocks and sections that don't contain any needed blocks.
    /// </summary>
    /// <param name="targetBlock">The block to isolate with its dependencies.</param>
    /// <exception cref="ArgumentException">Thrown if the block doesn't belong to this workflow hierarchy.</exception>
    public void IsolateBlockWithDependencies(Block targetBlock)
    {
        List<Block> allBlocks = GetAllBlocks();

        if (!allBlocks.Contains(targetBlock))
        {
            throw new ArgumentException(
                "Block does not belong to this workflow hierarchy.",
                nameof(targetBlock));
        }

        HashSet<string> neededReplacementTags = [];
        HashSet<Block> processedBlocks = [];
        CollectDependenciesRecursively(
            targetBlock,
            allBlocks,
            neededReplacementTags,
            processedBlocks);

        _ = neededReplacementTags.Add(targetBlock.ReplacementTag);

        HashSet<Block> blocksToKeep = [.. allBlocks.Where(b => neededReplacementTags
            .Contains(b.ReplacementTag))];

        var sectionsToRemove = new List<Section>();
        foreach (Section section in Sections)
        {
            if (section.IsSeparator)
            {
                continue;
            }

            List<Block> blocksToRemoveInSection = [.. section.Blocks
                .Where(b => !blocksToKeep.Contains(b))];

            foreach (Block block in blocksToRemoveInSection)
            {
                section.Remove(block);
            }

            if (section.Blocks.Count == 0)
            {
                sectionsToRemove.Add(section);
            }
            else
            {
                foreach (Block block in section.Blocks)
                {
                    block.IsOutputSource = true;

                    if (string.IsNullOrWhiteSpace(block.OutputTitleTemplate))
                    {
                        block.OutputTitleTemplate = !string.IsNullOrWhiteSpace(block.Name)
                            ? block.Name
                            : block.ReplacementTag;
                    }
                }

                section.NormalizeBlockOrdering();
            }
        }

        foreach (Section section in sectionsToRemove)
        {
            Remove(section);
        }

        NormalizeSectionOrdering();
    }

    private static void CollectDependenciesRecursively(
        Block block,
        List<Block> allBlocks,
        HashSet<string> collectedTags,
        HashSet<Block> processedBlocks)
    {
        if (processedBlocks.Contains(block))
        {
            return;
        }

        _ = processedBlocks.Add(block);

        List<string> dependencyTags = block.GetDependencyReplacementTags();

        List<Block> dependencyBlocks = [.. allBlocks.Where(b =>
            dependencyTags.Contains(b.ReplacementTag)
            || block.ReadsDocumentsCreatedBy(b))];

        foreach (Block dependencyBlock in dependencyBlocks)
        {
            _ = collectedTags.Add(dependencyBlock.ReplacementTag);

            CollectDependenciesRecursively(dependencyBlock, allBlocks, collectedTags, processedBlocks);
        }
    }
}

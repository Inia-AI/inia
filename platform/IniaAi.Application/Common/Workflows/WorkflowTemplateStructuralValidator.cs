using ErrorOr;
using IniaAi.Common;
using IniaAi.Common.Constants;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.InputRules;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

using IniaAi.Domain.Serialization;

namespace IniaAi.Application.Common.Workflows;

/// <inheritdoc cref="IWorkflowTemplateStructuralValidator"/>
public class WorkflowTemplateStructuralValidator : IWorkflowTemplateStructuralValidator
{
    private const string ROOT_PATH = "workflow";
    private const string SECTIONS_PROPERTY = "Sections";
    private const string BLOCKS_PROPERTY = "Blocks";
    private const string ORDER_PROPERTY = "Order";
    private const string REGRESSION_FIELDS_PROPERTY = "RegressionFields";
    private const string REGRESSION_SCHEMA_PROPERTY = "RegressionSchema";
    private const string SIGNATURE_FIELDS_PROPERTY = "SignatureFields";
    private const string DOCUMENT_CLASSES_PROPERTY = "DocumentClasses";
    private const string VARIABLES_PROPERTY = "Variables";

    private static readonly string[] _rootCollectionProperties = ["Sections", VARIABLES_PROPERTY, DOCUMENT_CLASSES_PROPERTY];
    private static readonly string[] _blockCollectionProperties = ["RagSettings", "TextInputs", "SupportedDocumentClasses"];

    private const string SHARED_COMPONENT_ID_PROPERTY = "SharedComponentId";
    private const string SHARED_COMPONENT_LINK_STATE_PROPERTY = "SharedComponentLinkState";
    private const string SHARED_COMPONENT_VERSION_HASH_PROPERTY = "SharedComponentVersionHash";
    private const string SHARED_COMPONENT_BINDINGS_PROPERTY = "SharedComponentBindings";
    private const string SHARED_COMPONENT_SLOT_STATES_PROPERTY = "SharedComponentSlotStates";
    private const string ENTRIES_PROPERTY = "Entries";

    private static readonly string[] _sharedComponentDependentProperties =
    [
        SHARED_COMPONENT_LINK_STATE_PROPERTY,
        SHARED_COMPONENT_VERSION_HASH_PROPERTY,
        SHARED_COMPONENT_BINDINGS_PROPERTY,
    ];

    private static readonly Regex _contentHashPattern = new(WorkflowCanonicalizer.CONTENT_HASH_PATTERN, RegexOptions.Compiled);

    public WorkflowStructuralValidationResult Validate(string workflowJson)
    {
        List<Error> errors = [];
        List<WorkflowStructuralValidationWarning> warnings = [];

        JObject? root = ParseRoot(workflowJson, errors);
        if (root is null)
        {
            return new WorkflowStructuralValidationResult(null, errors, warnings);
        }

        if (Workflow.DeclaresUnsupportedFormatVersion(root, out int declaredFormatVersion))
        {
            errors.Add(Error.Validation(
                nameof(Workflow.FormatVersion),
                Workflow.UnsupportedFormatVersionMessage(declaredFormatVersion)));
            return new WorkflowStructuralValidationResult(null, errors, warnings);
        }

        ValidateNullCollections(root, errors);
        if (errors.Count > 0)
        {
            // Workflow.FromJson would throw on null collections; stop before deserializing.
            return new WorkflowStructuralValidationResult(null, errors, warnings);
        }

        ValidateSharedComponentLinkShapes(root, errors);
        if (errors.Count > 0)
        {
            return new WorkflowStructuralValidationResult(null, errors, warnings);
        }

        CollectOrderWarnings(root, warnings);

        Workflow? workflow;
        try
        {
            workflow = Workflow.FromJson(workflowJson);
        }
        catch
        {
            // Backstop: the null-collection and null-element checks above catch the known crashers,
            // but a crafted payload must never escape the validator as an unhandled 500.
            errors.Add(Error.Validation(
                ROOT_PATH,
                "Workflow JSON could not be deserialized into a workflow template."));
            return new WorkflowStructuralValidationResult(null, errors, warnings);
        }

        if (workflow is null)
        {
            errors.Add(Error.Validation(
                ROOT_PATH,
                "Workflow JSON could not be deserialized into a workflow template."));
            return new WorkflowStructuralValidationResult(null, errors, warnings);
        }

        StripDefinitionOnlyEntries(workflow, warnings);
        WorkflowStructuralValidationResult entityResult = Validate(workflow);
        errors.AddRange(entityResult.Errors);
        warnings.AddRange(entityResult.Warnings);

        return new WorkflowStructuralValidationResult(workflow, errors, warnings);
    }

    public WorkflowStructuralValidationResult Validate(Workflow workflow)
    {
        List<Error> errors = [];
        List<WorkflowStructuralValidationWarning> warnings = [];

        if (Workflow.DeclaresUnsupportedFormatVersion(workflow.FormatVersion))
        {
            errors.Add(Error.Validation(
                nameof(Workflow.FormatVersion),
                Workflow.UnsupportedFormatVersionMessage(workflow.FormatVersion!.Value)));
        }

        if (string.IsNullOrWhiteSpace(workflow.Name))
        {
            errors.Add(Error.Validation("Name", "Workflow name must not be empty."));
        }

        bool blockPropertiesValid = ValidateBlockProperties(workflow, errors);
        ValidateWebAddresses(workflow, errors);
        ValidateReplacementTags(workflow, errors, warnings);
        ValidateDataOutputNames(workflow, errors, warnings);
        ValidateRegressionSchemas(workflow, errors);
        ValidateSignatureFields(workflow, errors, warnings);
        ValidateInputRules(workflow, errors, warnings);

        // Placeholder collection walks Block.Properties; a null Properties would NRE there,
        // so only run it once every block is known to have non-null Properties.
        if (blockPropertiesValid)
        {
            CollectPlaceholderWarnings(workflow, warnings);
        }

        if (PageOverviewCoverage.FindGap(workflow) is PageOverviewCoverage.Gap overviewGap)
        {
            warnings.Add(new WorkflowStructuralValidationWarning(DOCUMENT_CLASSES_PROPERTY, overviewGap.Message));
        }

        CollectOverviewAnswerFormatWarnings(
            workflow.Configuration?.PageOverviews,
            $"{nameof(Workflow.Configuration)}.{nameof(WorkflowConfiguration.PageOverviews)}",
            warnings);

        foreach (DocumentClass documentClass in workflow.DocumentClasses)
        {
            CollectOverviewAnswerFormatWarnings(
                documentClass.PageOverviews,
                $"{DOCUMENT_CLASSES_PROPERTY}[{documentClass.Key}].{nameof(DocumentClass.PageOverviews)}",
                warnings);
        }

        return new WorkflowStructuralValidationResult(workflow, errors, warnings);
    }

    private static void CollectOverviewAnswerFormatWarnings(
        PageOverviewPrompts? prompts,
        string path,
        List<WorkflowStructuralValidationWarning> warnings)
    {
        Add(nameof(PageOverviewPrompts.Pages), prompts?.Pages);
        Add(nameof(PageOverviewPrompts.SinglePage), prompts?.SinglePage);

        void Add(string member, PageOverviewPrompt? prompt)
        {
            if (prompt?.AnswerFormatProblem is string problem)
            {
                warnings.Add(new WorkflowStructuralValidationWarning($"{path}.{member}", problem));
            }
        }
    }

    private static void ValidateSharedComponentLinkShapes(JObject root, List<Error> errors)
    {
        if (root[SECTIONS_PROPERTY] is not JArray sections)
        {
            return;
        }

        for (int sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            if (sections[sectionIndex] is not JObject section)
            {
                continue;
            }

            string sectionPath = $"{SECTIONS_PROPERTY}[{sectionIndex}]";
            bool sectionHasId = ValidateLinkShape(section, sectionPath, errors);
            ValidateSectionOnlyVocabulary(section, sectionPath, sectionHasId, errors);

            if (section[BLOCKS_PROPERTY] is not JArray blocks)
            {
                continue;
            }

            for (int blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
            {
                if (blocks[blockIndex] is JObject block)
                {
                    string blockPath = $"{sectionPath}.{BLOCKS_PROPERTY}[{blockIndex}]";
                    _ = ValidateLinkShape(block, blockPath, errors);
                    RejectSectionOnlyVocabularyOnBlock(block, blockPath, errors);
                }
            }
        }
    }

    private static bool ValidateLinkShape(JObject node, string basePath, List<Error> errors)
    {
        JToken? idToken = node[SHARED_COMPONENT_ID_PROPERTY];
        if (idToken is not null && idToken.Type is not JTokenType.String and not JTokenType.Null)
        {
            errors.Add(Error.Validation(
                $"{basePath}.{SHARED_COMPONENT_ID_PROPERTY}",
                $"SharedComponentId must be a string. [{SharedComponentErrorCodes.INVALID_LINK_SHAPE}]"));
        }

        bool hasId = idToken is { Type: JTokenType.String }
            && !string.IsNullOrWhiteSpace((string?)idToken);

        foreach (string dependentProperty in _sharedComponentDependentProperties)
        {
            if (!hasId && node[dependentProperty] is { Type: not JTokenType.Null })
            {
                errors.Add(Error.Validation(
                    $"{basePath}.{dependentProperty}",
                    "SharedComponent link fields are server-managed and require a SharedComponentId " +
                    $"on the same node; remove the SharedComponent* fields. [{SharedComponentErrorCodes.INVALID_LINK_SHAPE}]"));
            }
        }

        if (hasId && node[SHARED_COMPONENT_LINK_STATE_PROPERTY] is null or { Type: JTokenType.Null })
        {
            errors.Add(Error.Validation(
                $"{basePath}.{SHARED_COMPONENT_LINK_STATE_PROPERTY}",
                "A SharedComponentId requires a SharedComponentLinkState on the same node; " +
                $"remove the SharedComponent* fields or round-trip them verbatim. [{SharedComponentErrorCodes.INVALID_LINK_SHAPE}]"));
        }

        if (node[SHARED_COMPONENT_LINK_STATE_PROPERTY] is { Type: not JTokenType.Null } stateToken)
        {
            string? state = stateToken.Type == JTokenType.String ? (string?)stateToken : null;
            if (state is not nameof(SharedComponentLinkState.Linked)
                and not nameof(SharedComponentLinkState.BrokenOut))
            {
                errors.Add(Error.Validation(
                    $"{basePath}.{SHARED_COMPONENT_LINK_STATE_PROPERTY}",
                    "SharedComponentLinkState must be \"Linked\" or \"BrokenOut\". " +
                    $"[{SharedComponentErrorCodes.INVALID_LINK_SHAPE}]"));
            }
        }

        if (node[SHARED_COMPONENT_VERSION_HASH_PROPERTY] is { Type: not JTokenType.Null } hashToken
            && (hashToken.Type != JTokenType.String || !_contentHashPattern.IsMatch((string)hashToken!)))
        {
            errors.Add(Error.Validation(
                $"{basePath}.{SHARED_COMPONENT_VERSION_HASH_PROPERTY}",
                "SharedComponentVersionHash must be a 64-character lowercase hex content hash. " +
                $"[{SharedComponentErrorCodes.INVALID_LINK_SHAPE}]"));
        }

        if (node[SHARED_COMPONENT_BINDINGS_PROPERTY] is { Type: not JTokenType.Null } bindingsToken
            && (bindingsToken is not JObject bindings
                || bindings.Properties().Any(p => p.Value.Type != JTokenType.String)))
        {
            errors.Add(Error.Validation(
                $"{basePath}.{SHARED_COMPONENT_BINDINGS_PROPERTY}",
                "SharedComponentBindings must be a JSON object mapping definition slot tags to " +
                $"instance tags (string values only). [{SharedComponentErrorCodes.INVALID_LINK_SHAPE}]"));
        }

        return hasId;
    }

    /// <summary>
    /// Entries is definition-payload vocabulary: a template carries materialized blocks, never entry
    /// lists. A content GET can still serve one on a linked section whose stored payload kept it, so
    /// dropping it here keeps the documented read-edit-write round-trip working instead of rejecting
    /// the caller's own bytes back at them. The warning stops the drop being silent.
    /// </summary>
    private static void StripDefinitionOnlyEntries(
        Workflow workflow,
        List<WorkflowStructuralValidationWarning> warnings)
    {
        List<Section> sections = [.. workflow.Sections];
        for (int sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            if (sections[sectionIndex].Entries is null)
            {
                continue;
            }

            sections[sectionIndex].Entries = null;
            warnings.Add(new WorkflowStructuralValidationWarning(
                $"{SECTIONS_PROPERTY}[{sectionIndex}].{ENTRIES_PROPERTY}",
                "Entries is definition-payload vocabulary and was dropped; a workflow template carries "
                + "materialized blocks, never entry lists. Its slots are described by the shared "
                + "component's own definition."));
        }
    }

    private static void ValidateSectionOnlyVocabulary(
        JObject section,
        string sectionPath,
        bool sectionHasId,
        List<Error> errors)
    {
        JToken? slotStatesToken = section[SHARED_COMPONENT_SLOT_STATES_PROPERTY];
        if (slotStatesToken is null || slotStatesToken.Type == JTokenType.Null)
        {
            return;
        }

        string slotStatesPath = $"{sectionPath}.{SHARED_COMPONENT_SLOT_STATES_PROPERTY}";
        if (!sectionHasId)
        {
            errors.Add(Error.Validation(
                slotStatesPath,
                "SharedComponentSlotStates records per-entry overrides of a shared-component copy and "
                + $"requires a SharedComponentId on the same section. [{SharedComponentErrorCodes.INVALID_LINK_SHAPE}]"));

            return;
        }

        if (slotStatesToken is not JObject slotStates)
        {
            errors.Add(Error.Validation(
                slotStatesPath,
                "SharedComponentSlotStates must be a JSON object mapping slot tags to override states. "
                + $"[{SharedComponentErrorCodes.INVALID_LINK_SHAPE}]"));

            return;
        }

        foreach (JProperty state in slotStates.Properties())
        {
            string? value = state.Value.Type == JTokenType.String ? (string?)state.Value : null;
            if (value is not nameof(SharedComponentSlotState.Hidden)
                and not nameof(SharedComponentSlotState.BrokenOut))
            {
                errors.Add(Error.Validation(
                    slotStatesPath,
                    $"Slot '{state.Name}' must map to \"Hidden\" or \"BrokenOut\". "
                    + $"[{SharedComponentErrorCodes.INVALID_LINK_SHAPE}]"));
            }
        }
    }

    private static void RejectSectionOnlyVocabularyOnBlock(JObject block, string blockPath, List<Error> errors)
    {
        if (block[SHARED_COMPONENT_SLOT_STATES_PROPERTY] is { Type: not JTokenType.Null })
        {
            errors.Add(Error.Validation(
                $"{blockPath}.{SHARED_COMPONENT_SLOT_STATES_PROPERTY}",
                "Only sections carry per-entry override records. "
                + $"[{SharedComponentErrorCodes.INVALID_LINK_SHAPE}]"));
        }
    }

    private static JObject? ParseRoot(string workflowJson, List<Error> errors)
    {
        if (string.IsNullOrWhiteSpace(workflowJson))
        {
            errors.Add(Error.Validation(ROOT_PATH, "Workflow JSON must not be empty."));
            return null;
        }

        JToken token;
        try
        {
            token = JToken.Parse(workflowJson);
        }
        catch (Exception ex)
        {
            errors.Add(Error.Validation(ROOT_PATH, $"Workflow JSON is not valid JSON: {ex.Message}"));
            return null;
        }

        if (token is not JObject root)
        {
            errors.Add(Error.Validation(ROOT_PATH, "Workflow JSON must be a JSON object."));
            return null;
        }

        return root;
    }

    /// <summary>
    /// An explicit JSON null on a collection property nulls the entity collection
    /// (the initializers only cover absent properties), which crashes
    /// <see cref="Workflow.FromJson(string, Newtonsoft.Json.JsonSerializerSettings?)"/> consumers downstream.
    /// </summary>
    private static void ValidateNullCollections(JObject root, List<Error> errors)
    {
        foreach (string property in _rootCollectionProperties)
        {
            if (IsExplicitNull(root, property))
            {
                errors.Add(Error.Validation(property, $"'{property}' must be an array, not null. Omit the property to mean empty."));
                continue;
            }

            ReportNullElements(root[property] as JArray, property, errors);
        }

        if (root[SECTIONS_PROPERTY] is not JArray sections)
        {
            return;
        }

        for (int sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            if (sections[sectionIndex] is not JObject section)
            {
                continue;
            }

            string sectionPath = $"{SECTIONS_PROPERTY}[{sectionIndex}]";

            if (IsExplicitNull(section, BLOCKS_PROPERTY))
            {
                errors.Add(Error.Validation($"{sectionPath}.{BLOCKS_PROPERTY}", "'Blocks' must be an array, not null. Omit the property to mean empty."));
            }

            if (section[BLOCKS_PROPERTY] is not JArray blocks)
            {
                continue;
            }

            ReportNullElements(blocks, $"{sectionPath}.{BLOCKS_PROPERTY}", errors);

            for (int blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
            {
                if (blocks[blockIndex] is not JObject block)
                {
                    continue;
                }

                foreach (string property in _blockCollectionProperties)
                {
                    if (IsExplicitNull(block, property))
                    {
                        errors.Add(Error.Validation(
                            $"{sectionPath}.{BLOCKS_PROPERTY}[{blockIndex}].{property}",
                            $"'{property}' must be an array, not null. Omit the property to mean empty."));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Every block must carry a non-null <c>Properties</c> object. A null (or omitted, which the
    /// converter also yields as null) Properties is not just invalid data — it NREs the placeholder
    /// walk and every execution-time consumer. Reported as a hard error with the block's path.
    /// </summary>
    private static bool ValidateBlockProperties(Workflow workflow, List<Error> errors)
    {
        bool valid = true;
        List<Section> sections = [.. workflow.Sections];
        for (int sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            List<Block> blocks = [.. sections[sectionIndex].Blocks];
            for (int blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
            {
                if (blocks[blockIndex].Properties is null)
                {
                    errors.Add(Error.Validation(
                        $"{SECTIONS_PROPERTY}[{sectionIndex}].{BLOCKS_PROPERTY}[{blockIndex}].Properties",
                        "Block 'Properties' must be a non-null object."));
                    valid = false;
                }
            }
        }

        return valid;
    }

    /// <summary>
    /// A Web API call whose host a placeholder could choose is refused, since a value can come from a
    /// model's answer about a stranger's document and send the request anywhere.
    /// </summary>
    private static void ValidateWebAddresses(Workflow workflow, List<Error> errors)
    {
        List<Section> sections = [.. workflow.Sections];
        for (int sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            List<Block> blocks = [.. sections[sectionIndex].Blocks];
            for (int blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
            {
                if (blocks[blockIndex].Properties is not WebApiCallProperties webCall)
                {
                    continue;
                }

                foreach ((string field, string label, string? template) in webCall.AddressTemplates())
                {
                    if (WebApiCallProperties.FixedHostProblem(blocks[blockIndex].Name, label, template) is string problem)
                    {
                        errors.Add(Error.Validation(
                            $"{SECTIONS_PROPERTY}[{sectionIndex}].{BLOCKS_PROPERTY}[{blockIndex}].Properties.{field}",
                            problem));
                    }
                }
            }
        }
    }

    private static bool IsExplicitNull(JObject parent, string propertyName)
    {
        JToken? token = parent[propertyName];
        return token is not null && token.Type == JTokenType.Null;
    }

    /// <summary>
    /// A null element inside a collection (e.g. <c>"Sections":[null]</c>) survives the whole-collection
    /// null check, then NREs inside <see cref="Workflow.FromJson(string, Newtonsoft.Json.JsonSerializerSettings?)"/>'s
    /// ordering/navigation fixups. Report each null element with its indexed path before deserializing.
    /// </summary>
    private static void ReportNullElements(JArray? array, string arrayPath, List<Error> errors)
    {
        if (array is null)
        {
            return;
        }

        for (int index = 0; index < array.Count; index++)
        {
            if (array[index] is null || array[index].Type == JTokenType.Null)
            {
                errors.Add(Error.Validation($"{arrayPath}[{index}]", $"'{arrayPath}[{index}]' must not be null."));
            }
        }
    }

    /// <summary>
    /// Order issues are warnings, not errors: <see cref="Workflow.FromJson(string, Newtonsoft.Json.JsonSerializerSettings?)"/>
    /// silently renumbers sections and blocks on load, so the checks run against the raw JSON.
    /// </summary>
    private static void CollectOrderWarnings(JObject root, List<WorkflowStructuralValidationWarning> warnings)
    {
        if (root[SECTIONS_PROPERTY] is not JArray sections)
        {
            return;
        }

        CheckOrderValues(sections, SECTIONS_PROPERTY, warnings);

        for (int sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            if (sections[sectionIndex] is not JObject section || section[BLOCKS_PROPERTY] is not JArray blocks)
            {
                continue;
            }

            CheckOrderValues(blocks, $"{SECTIONS_PROPERTY}[{sectionIndex}].{BLOCKS_PROPERTY}", warnings);
        }
    }

    private static void CheckOrderValues(JArray items, string path, List<WorkflowStructuralValidationWarning> warnings)
    {
        List<int> orders = [];
        foreach (JToken item in items)
        {
            if (item is JObject obj && obj[ORDER_PROPERTY] is JToken orderToken && orderToken.Type == JTokenType.Integer)
            {
                orders.Add(orderToken.Value<int>());
            }
        }

        if (orders.Any(order => order < 0))
        {
            warnings.Add(new WorkflowStructuralValidationWarning(path, "Negative Order values found; orders will be normalized on save."));
        }

        if (orders.Count != orders.Distinct().Count())
        {
            warnings.Add(new WorkflowStructuralValidationWarning(path, "Duplicate Order values found; orders will be normalized on save."));
        }
    }

    /// <summary>
    /// A data output name longer than the column width is a blocking error, as an overlong tag is.
    /// Two blocks sending data under one name hand a reading system two values for one key, so
    /// that is a warning, as a shared tag is.
    /// </summary>
    private static void ValidateDataOutputNames(
        Workflow workflow,
        List<Error> errors,
        List<WorkflowStructuralValidationWarning> warnings)
    {
        List<Section> sections = [.. workflow.Sections];
        for (int sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            foreach (Block block in sections[sectionIndex].Blocks
                .Where(block => block.DataOutputName?.Length > Block.MAX_REPLACEMENT_TAG_LENGTH))
            {
                errors.Add(Error.Validation(
                    $"{SECTIONS_PROPERTY}[{sectionIndex}].Blocks[{block.Order}].DataOutputName",
                    $"DataOutputName is {block.DataOutputName!.Length} characters; the maximum is {Block.MAX_REPLACEMENT_TAG_LENGTH}."));
            }
        }

        foreach (IGrouping<string, Block> group in workflow.GetAllBlocks()
            .Where(block => block.IsDataOutput)
            .GroupBy(block => block.DataOutputName!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1))
        {
            warnings.Add(new WorkflowStructuralValidationWarning(
                SECTIONS_PROPERTY,
                $"DataOutputName '{group.Key}' is used by {group.Count()} blocks; the run returns each under the same name."));
        }
    }

    /// <summary>
    /// The execution engine resolves a tag reference to "the first block with that tag
    /// that produced a response", so two blocks sharing a tag (in the same section or
    /// across sections) make references resolve to the first responder. This is a
    /// legitimate authoring pattern (duplicated sections, branch variants), so it is
    /// surfaced as a warning rather than blocking the save. A tag longer than the
    /// column width is a blocking error, since persisting it would fail in the database.
    /// </summary>
    private static void ValidateReplacementTags(
        Workflow workflow,
        List<Error> errors,
        List<WorkflowStructuralValidationWarning> warnings)
    {
        List<Section> sections = [.. workflow.Sections];
        for (int sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            List<Block> overlongTagged = [.. sections[sectionIndex].Blocks
                .Where(block => block.ReplacementTag.Length > Block.MAX_REPLACEMENT_TAG_LENGTH)];

            foreach (Block block in overlongTagged)
            {
                errors.Add(Error.Validation(
                    $"{SECTIONS_PROPERTY}[{sectionIndex}].Blocks[{block.Order}].ReplacementTag",
                    $"ReplacementTag is {block.ReplacementTag.Length} characters; the maximum is {Block.MAX_REPLACEMENT_TAG_LENGTH}."));
            }

            IEnumerable<IGrouping<string, Block>> duplicates = sections[sectionIndex].Blocks
                .Where(block => !string.IsNullOrWhiteSpace(block.ReplacementTag))
                .GroupBy(block => block.ReplacementTag)
                .Where(group => group.Count() > 1);

            foreach (IGrouping<string, Block> group in duplicates)
            {
                warnings.Add(new WorkflowStructuralValidationWarning(
                    $"{SECTIONS_PROPERTY}[{sectionIndex}]",
                    $"ReplacementTag '{group.Key}' is used by {group.Count()} blocks in the same section; references resolve to the first block that produced a response."));
            }
        }

        IEnumerable<string> crossSectionDuplicates = sections
            .SelectMany((section, index) => section.Blocks
                .Where(block => !string.IsNullOrWhiteSpace(block.ReplacementTag))
                .Select(block => block.ReplacementTag)
                .Distinct()
                .Select(tag => (Tag: tag, SectionIndex: index)))
            .GroupBy(entry => entry.Tag)
            .Where(group => group.Select(entry => entry.SectionIndex).Distinct().Count() > 1)
            .Select(group => group.Key);

        foreach (string tag in crossSectionDuplicates)
        {
            warnings.Add(new WorkflowStructuralValidationWarning(
                SECTIONS_PROPERTY,
                $"ReplacementTag '{tag}' is used in multiple sections; references resolve to the first block that produced a response."));
        }
    }

    /// <summary>
    /// Conservative unresolved-reference detection: only plain identifier tags are checked,
    /// because the full tag grammar (nested parameterized tags, loop list titles) cannot be
    /// resolved statically. Always warnings, never errors.
    /// </summary>
    private static void CollectPlaceholderWarnings(Workflow workflow, List<WorkflowStructuralValidationWarning> warnings)
    {
        // Signature fields stay excluded: their values inject only for signature-bearing blocks,
        // so a {SignatureField} reference on any other block is a legitimate unmatched-reference warning.
        HashSet<string> knownTags = WorkflowKnownBaseTags.Collect(workflow, includeSignatureFields: false);

        List<Section> sections = [.. workflow.Sections];
        for (int sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            List<Block> blocks = [.. sections[sectionIndex].Blocks];
            for (int blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
            {
                IEnumerable<string> unknownTags = blocks[blockIndex].GetDependencyReplacementTags()
                    .Select(tag => tag.GetBaseReplacementTag())
                    .Where(IsPlainIdentifier)
                    .Where(tag => !knownTags.Contains(tag));

                foreach (string tag in unknownTags)
                {
                    warnings.Add(new WorkflowStructuralValidationWarning(
                        $"{SECTIONS_PROPERTY}[{sectionIndex}].{BLOCKS_PROPERTY}[{blockIndex}]",
                        $"Block '{blocks[blockIndex].ReplacementTag}' references '{{{tag}}}', which does not match any block, variable, document class, or reserved input."));
                }
            }
        }
    }

    private static bool IsPlainIdentifier(string tag)
    {
        return tag.Length > 0 && tag.All(c => char.IsLetterOrDigit(c) || c is '_' or '-');
    }

    /// <summary>
    /// True for validation errors produced by the regression-field rules (workflow-level definitions
    /// or block bindings). The editor save gate blocks only on these, so it never retroactively
    /// enforces unrelated structural rules that were previously API-only.
    /// </summary>
    public static bool IsRegressionError(Error error)
    {
        return error.Code.Contains(REGRESSION_FIELDS_PROPERTY, StringComparison.Ordinal)
            || error.Code.Contains(REGRESSION_SCHEMA_PROPERTY, StringComparison.Ordinal);
    }

    /// <summary>True for the error about a rule that cannot be evaluated at all.</summary>
    public static bool IsInputRuleError(Error error)
    {
        return error.Code.Contains($".{nameof(WorkflowVariable.InputRules)}[", StringComparison.Ordinal);
    }

    /// <summary>True for the warning about a rule that runs will skip.</summary>
    public static bool IsInputRuleWarning(WorkflowStructuralValidationWarning warning)
    {
        return warning.Path.EndsWith($".{nameof(WorkflowVariable.InputRules)}", StringComparison.Ordinal);
    }

    /// <summary>
    /// A rule that cannot be evaluated at all blocks the save. A rule that names something the
    /// workflow does not have saves with a warning, and runs skip it until it is fixed.
    /// </summary>
    private static void ValidateInputRules(
        Workflow workflow,
        List<Error> errors,
        List<WorkflowStructuralValidationWarning> warnings)
    {
        foreach (WorkflowVariable variable in workflow.Variables)
        {
            for (int index = 0; index < variable.InputRules.Count; index++)
            {
                if (VariableInputRuleCheck.IncompletenessOf(variable.InputRules[index]) is string incomplete)
                {
                    errors.Add(Error.Validation(
                        InputRulePath(variable, index),
                        $"A rule on \"{VariableInputRuleCheck.NameOf(variable)}\" cannot be saved because {incomplete}. Complete the rule or remove it."));
                }
            }
        }

        foreach (SkippedInputRule stale in VariableInputRuleCheck.FindStale(workflow))
        {
            if (stale.Kind is InputRuleProblem.Incomplete)
            {
                continue;
            }

            warnings.Add(new WorkflowStructuralValidationWarning(
                $"{VARIABLES_PROPERTY}[{stale.ReplacementTag}].{nameof(WorkflowVariable.InputRules)}",
                VariableInputRuleCheck.SaveWarningOf(stale)));
        }
    }

    private static string InputRulePath(WorkflowVariable variable, int index)
    {
        return $"{VARIABLES_PROPERTY}[{variable.ReplacementTag}].{nameof(WorkflowVariable.InputRules)}[{index}]";
    }

    /// <summary>
    /// True for validation errors produced by the signature-field rules, so the editor save gate
    /// can block on them without retroactively enforcing unrelated structural rules.
    /// </summary>
    public static bool IsSignatureError(Error error)
    {
        return error.Code.Contains(SIGNATURE_FIELDS_PROPERTY, StringComparison.Ordinal);
    }

    /// <summary>
    /// True for the errors of a Web API call address whose host a placeholder could choose, so the
    /// editor's save gate refuses them as the API does.
    /// </summary>
    public static bool IsWebAddressError(Error error)
    {
        return error.Code.EndsWith($".Properties.{nameof(WebApiCallProperties.UrlTemplate)}", StringComparison.Ordinal)
            || error.Code.EndsWith($".Properties.{nameof(WebApiCallProperties.PollCompletionUrlTemplate)}", StringComparison.Ordinal);
    }

    /// <summary>
    /// Signature fields are defined at workflow level and bound by blocks implicitly — a block
    /// references a field iff its templates contain the <c>{Name}</c> token. Validate that
    /// definitions are well-formed and unique, that no name collides with a block ReplacementTag or
    /// workflow variable tag (the token would be ambiguous), and that workflow blocks carry no
    /// residual inline definition dicts (those are hoisted on load; only block-template library
    /// rows keep them). An unreferenced definition is a warning, not an error — it is harmless
    /// but stale.
    /// </summary>
    private static void ValidateSignatureFields(
        Workflow workflow,
        List<Error> errors,
        List<WorkflowStructuralValidationWarning> warnings)
    {
        var blockTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Block block in workflow.GetAllBlocks())
        {
            if (!string.IsNullOrWhiteSpace(block.ReplacementTag))
            {
                _ = blockTags.Add(block.ReplacementTag);
            }
        }

        var variableTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (WorkflowVariable variable in workflow.Variables)
        {
            if (!string.IsNullOrWhiteSpace(variable.ReplacementTag))
            {
                _ = variableTags.Add(variable.ReplacementTag);
            }
        }

        var reservedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DocumentClass documentClass in workflow.DocumentClasses)
        {
            if (!string.IsNullOrWhiteSpace(documentClass.Key))
            {
                _ = reservedTags.Add(documentClass.Key);
            }
        }

        foreach (string reservedTag in SharedInputKeys.ReservedReplacementTags)
        {
            _ = reservedTags.Add(reservedTag);
        }

        var definitionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<WorkflowSignatureField> definitions = [.. workflow.SignatureFields];
        for (int definitionIndex = 0; definitionIndex < definitions.Count; definitionIndex++)
        {
            WorkflowSignatureField definition = definitions[definitionIndex];
            string definitionPath = $"{SIGNATURE_FIELDS_PROPERTY}[{definitionIndex}]";

            if (string.IsNullOrWhiteSpace(definition.Name))
            {
                errors.Add(Error.Validation(definitionPath, "Signature field must have a name."));
                continue;
            }

            if (!definitionNames.Add(definition.Name))
            {
                errors.Add(Error.Validation(
                    definitionPath,
                    $"Signature field '{definition.Name}' is defined more than once. Field names must be unique per workflow."));
            }

            if (blockTags.Contains(definition.Name))
            {
                errors.Add(Error.Validation(
                    definitionPath,
                    $"Signature field '{definition.Name}' collides with a block ReplacementTag. The {{{definition.Name}}} token would be ambiguous."));
            }

            if (variableTags.Contains(definition.Name))
            {
                errors.Add(Error.Validation(
                    definitionPath,
                    $"Signature field '{definition.Name}' collides with a workflow variable tag. The {{{definition.Name}}} token would be ambiguous."));
            }

            if (reservedTags.Contains(definition.Name))
            {
                errors.Add(Error.Validation(
                    definitionPath,
                    $"Signature field '{definition.Name}' collides with a reserved input tag or document class key. The {{{definition.Name}}} token would be ambiguous."));
            }
        }

        if (definitions.Count > 0)
        {
            Dictionary<string, List<Block>> references = workflow.GetSignatureFieldReferences();
            for (int definitionIndex = 0; definitionIndex < definitions.Count; definitionIndex++)
            {
                WorkflowSignatureField definition = definitions[definitionIndex];
                if (string.IsNullOrWhiteSpace(definition.Name)
                    || (references.TryGetValue(definition.Name, out List<Block>? binders) && binders.Count > 0))
                {
                    continue;
                }

                warnings.Add(new WorkflowStructuralValidationWarning(
                    $"{SIGNATURE_FIELDS_PROPERTY}[{definitionIndex}]",
                    $"Signature field '{definition.Name}' is not referenced by any block template. Add {{{definition.Name}}} to a prompt or delete the definition."));
            }
        }

        List<Section> sections = [.. workflow.Sections];
        for (int sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            List<Block> blocks = [.. sections[sectionIndex].Blocks];
            for (int blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
            {
                if (blocks[blockIndex].Properties is IHasSignatureFields { SignatureFields.Count: > 0 })
                {
                    errors.Add(Error.Validation(
                        $"{SECTIONS_PROPERTY}[{sectionIndex}].{BLOCKS_PROPERTY}[{blockIndex}].{SIGNATURE_FIELDS_PROPERTY}",
                        $"Block '{blocks[blockIndex].ReplacementTag}' carries an inline signature field dict. "
                        + "Definitions live on the workflow; inline dicts are hoisted on load and must not be re-introduced."));
                }
            }
        }
    }

    /// <summary>
    /// Regression fields are defined at workflow level (name/type/tolerance) and bound by blocks
    /// (source/locator keyed by field name in the block's RegressionSchema JSON). Validate at save
    /// time that definitions are well-formed and unique, that every binding names a defined field,
    /// that no field is bound by more than one block, and that declared extraction sources are known
    /// (with a compilable pattern for regex).
    /// </summary>
    private static void ValidateRegressionSchemas(Workflow workflow, List<Error> errors)
    {
        var definitionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<WorkflowRegressionField> definitions = [.. workflow.RegressionFields];
        for (int definitionIndex = 0; definitionIndex < definitions.Count; definitionIndex++)
        {
            WorkflowRegressionField definition = definitions[definitionIndex];
            string definitionPath = $"{REGRESSION_FIELDS_PROPERTY}[{definitionIndex}]";

            if (string.IsNullOrWhiteSpace(definition.Name))
            {
                errors.Add(Error.Validation(definitionPath, "Regression field must have a name."));
                continue;
            }

            if (!definitionNames.Add(definition.Name))
            {
                errors.Add(Error.Validation(
                    definitionPath,
                    $"Regression field '{definition.Name}' is defined more than once. Field names must be unique per workflow."));
            }

            if (!RegressionFieldTypes.IsKnown(definition.Type))
            {
                errors.Add(Error.Validation(
                    definitionPath,
                    $"Regression field '{definition.Name}' has unknown type '{definition.Type}'. Expected one of: {string.Join(", ", RegressionFieldTypes.ALL)}."));
            }

            if (definition.Tolerance < 0)
            {
                errors.Add(Error.Validation(
                    definitionPath,
                    $"Regression field '{definition.Name}' has a negative tolerance."));
            }
        }

        // Field name -> label of the block that already binds it (one block per field).
        var bindersByField = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        List<Section> sections = [.. workflow.Sections];
        for (int sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            List<Block> blocks = [.. sections[sectionIndex].Blocks];
            for (int blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
            {
                Block block = blocks[blockIndex];
                if (string.IsNullOrWhiteSpace(block.RegressionSchema))
                {
                    continue;
                }

                string path = $"{SECTIONS_PROPERTY}[{sectionIndex}].{BLOCKS_PROPERTY}[{blockIndex}].{REGRESSION_SCHEMA_PROPERTY}";
                string blockLabel = string.IsNullOrWhiteSpace(block.ReplacementTag) ? block.Name : block.ReplacementTag;

                if (!RegressionSchemaBindings.TryParse(block.RegressionSchema, out Dictionary<string, RegressionFieldBinding> bindings))
                {
                    errors.Add(Error.Validation(path, "RegressionSchema is not valid JSON."));
                    continue;
                }

                foreach ((string fieldName, RegressionFieldBinding binding) in bindings)
                {
                    if (!definitionNames.Contains(fieldName))
                    {
                        errors.Add(Error.Validation(
                            path,
                            $"Block '{blockLabel}' binds regression field '{fieldName}', but the workflow defines no field with that name."));
                    }

                    if (bindersByField.TryGetValue(fieldName, out string? firstBinder))
                    {
                        errors.Add(Error.Validation(
                            path,
                            $"Regression field '{fieldName}' is bound by both block '{firstBinder}' and block '{blockLabel}'. A field can be produced by only one block."));
                    }
                    else
                    {
                        bindersByField[fieldName] = blockLabel;
                    }

                    ValidateBindingSource(path, fieldName, binding, errors);
                }
            }
        }
    }

    private static void ValidateBindingSource(
        string path,
        string fieldName,
        RegressionFieldBinding binding,
        List<Error> errors)
    {
        if (binding?.Source is null)
        {
            return;
        }

        if (!RegressionSchemaSources.IsKnown(binding.Source))
        {
            errors.Add(Error.Validation(
                path,
                $"Field '{fieldName}' has unknown extraction source '{binding.Source}'. Expected one of: {string.Join(", ", RegressionSchemaSources.ALL)}."));
            return;
        }

        // Regex requires a pattern that compiles; xml/json default their locator to the field name;
        // llm needs no locator.
        if (!string.Equals(binding.Source, RegressionSchemaSources.REGEX, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(binding.Locator))
        {
            errors.Add(Error.Validation(
                path,
                $"Field '{fieldName}' uses source 'regex' but supplies no pattern (locator)."));
            return;
        }

        try
        {
            _ = System.Text.RegularExpressions.Regex.Match(string.Empty, binding.Locator);
        }
        catch (System.Text.RegularExpressions.RegexParseException ex)
        {
            errors.Add(Error.Validation(
                path,
                $"Field '{fieldName}' has an invalid regex pattern: {ex.Message}"));
        }
    }
}

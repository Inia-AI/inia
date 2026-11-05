using IniaAi.Common;
using IniaAi.Common.Constants;
using IniaAi.Core.Workflows.Graph;
using IniaAi.Core.Workflows.Scheduling;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Core.Workflows.Dependencies;

/// <summary>
/// Resolves every placeholder the way the runtime does (<c>WorkflowHandler.CollectAllInputs</c>
/// then <c>PopulatePlaceholderStringFromSection</c>): the block's own TextInputs and signature
/// fields first, then the counterparty, organization and peer-company entities, then block tags,
/// then reserved and loop inputs. Whatever is left is unresolved.
/// </summary>
internal sealed class DependencyMapBuilder
{
    private const string VARIABLE_PREFIX = "variable:";
    private const string SIGNATURE_PREFIX = "signature:";
    private const string SYSTEM_PREFIX = "system:";
    private const string LOOP_PREFIX = "loop:";
    private const string UNRESOLVED_PREFIX = "unresolved:";

    private static readonly IReadOnlySet<string> _noParameters = new HashSet<string>();

    private readonly List<DependencyBlock> _blocks = [];
    private readonly Dictionary<string, DependencyBlock> _blocksById = new(StringComparer.Ordinal);
    private readonly List<Occurrence> _occurrences = [];
    private readonly Dictionary<string, List<Producer>> _producersByTag = new(StringComparer.Ordinal);
    private readonly List<DependencyInput> _inputs = [];
    private readonly Dictionary<string, DependencyInput> _inputsById = new(StringComparer.Ordinal);
    private readonly List<EdgeAccumulator> _edges = [];
    private readonly Dictionary<(string Source, string Target, DependencyEdgeKind Kind), EdgeAccumulator> _edgesByKey = [];
    private readonly List<DependencyTagRead> _tagReads = [];
    private readonly List<OrderingLink> _orderingLinks = [];
    private readonly List<DependencyIssue> _orderingIssues = [];
    private readonly HashSet<(DependencyIssueKind Kind, string ReaderId, string ProducerId, string Tag)> _orderingIssueKeys = [];
    private readonly List<(Occurrence Reader, string Placeholder)> _counterpartyReads = [];
    private readonly Dictionary<string, List<string>> _unresolvedReaders = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loopTitles = new(StringComparer.Ordinal);
    private List<DependencyVerificationGroup> _verificationGroups = [];
    private Dictionary<OrderingNode, int>? _segmentOf;

    public static WorkflowDependencyMap ForTemplate(Workflow workflow)
    {
        var builder = new DependencyMapBuilder();
        builder.RegisterVariables(workflow.Variables);

        List<Section> sections = [.. workflow.Sections.NonSeparator().OrderBy(static section => section.Order)];
        List<SectionScope> scopes = [.. sections.Select(ReadScope)];

        List<(Block Block, int SectionOrder, IReadOnlyList<string> VariantKeys)> placed = [];
        foreach ((Section section, List<SectionVariant> variants, _) in scopes)
        {
            List<string> keys = [.. variants.Select(static variant => variant.Key)];
            placed.AddRange(section.Blocks
                .OrderBy(static block => block.Order)
                .Select(block => (block, section.Order, (IReadOnlyList<string>)keys)));
        }

        builder.PlaceBlocks(placed);
        builder._segmentOf = IndexDispatchSegments(scopes);

        IReadOnlyCollection<WorkflowSignatureField> signatureFields = [.. workflow.SignatureFields];
        foreach (SectionScope scope in scopes)
        {
            builder.AddSectionOccurrences(scope, signatureFields);
        }

        builder.ResolveAll();

        foreach ((Section section, List<SectionVariant> variants, _) in scopes)
        {
            builder.AddGateEdges(section, variants);
        }

        return builder.Freeze();
    }

    public static WorkflowDependencyMap ForDispatch(
        IReadOnlyDictionary<int, List<Block>> blocksBySectionOrder,
        IReadOnlyList<WorkflowVariable> variables)
    {
        var builder = new DependencyMapBuilder();
        builder.RegisterVariables(variables);

        List<(Block Block, int SectionOrder, IReadOnlyList<string> VariantKeys)> placed = [.. blocksBySectionOrder
            .OrderBy(static entry => entry.Key)
            .SelectMany(static entry => entry.Value
                .OrderBy(static block => block.Order)
                .Select(block => (block, entry.Key, (IReadOnlyList<string>)[])))];

        builder.PlaceBlocks(placed);

        foreach (DependencyBlock node in builder._blocks)
        {
            builder.AddPlainOccurrence(node, SignatureFieldsOf(node.Block));
        }

        builder.ResolveAll();

        return builder.Freeze();
    }

    /// <summary>
    /// The variants of a parameterized section as the run normalizes them, worked out without
    /// normalizing a legacy config in place (the editor's workflow must not change because it was
    /// drawn). No variants when the section is not parameterized or its config does not parse,
    /// which is how the run treats it too.
    /// </summary>
    private static SectionScope ReadScope(Section section)
    {
        ParameterizedSectionPlan? plan = ParameterizedSectionNormalizer.Plan(section);

        return new SectionScope(section, plan?.Config?.Variants ?? [], plan?.BlockClasses);
    }

    /// <summary>
    /// The dispatch segment of every block occurrence, as the run will segment the workflow once
    /// its parameterized sections are expanded: each variant is a section of its own, gated by its
    /// stop gate and producing its suffixed tags. Only the base fence rule is visible here.
    /// </summary>
    private static Dictionary<OrderingNode, int> IndexDispatchSegments(List<SectionScope> scopes)
    {
        List<(Section Section, SectionVariant? Variant)> units = [];
        foreach (SectionScope scope in scopes)
        {
            if (scope.Variants.Count == 0)
            {
                units.Add((scope.Section, null));
                continue;
            }

            units.AddRange(scope.Variants.Select(variant => (scope.Section, (SectionVariant?)variant)));
        }

        List<(List<(Section Section, SectionVariant? Variant)> Units, bool IsLoop)> segments = DispatchSegments.Group(
            units,
            static unit => unit.Variant is null
                ? DispatchSegments.IsLoopSection(unit.Section)
                : DispatchSegments.HoldsLoopStart(unit.Section),
            static unit => unit.Variant is null
                ? unit.Section.GetAllPlaceholders()
                : unit.Variant.SectionStopGate?.GetPlaceholders() ?? [],
            static unit => unit.Variant is null
                ? unit.Section.Blocks.SelectMany(DispatchSegments.ProducedTags)
                : unit.Section.Blocks
                    .Where(static block => !string.IsNullOrWhiteSpace(block.ReplacementTag))
                    .Select(block => $"{block.ReplacementTag}_{unit.Variant.Key}"),
            static unit => unit.Section.Blocks.Any(DispatchSegments.IsBaseExecutionOrderFence));

        Dictionary<OrderingNode, int> segmentOf = [];
        for (int segment = 0; segment < segments.Count; segment++)
        {
            foreach ((Section section, SectionVariant? variant) in segments[segment].Units)
            {
                foreach (Block block in section.Blocks)
                {
                    segmentOf[new OrderingNode(block.Id, variant?.Key)] = segment;
                }
            }
        }

        return segmentOf;
    }

    /// <summary>The definitions the runtime injects from: the block's own workflow, as it reads them.</summary>
    private static IReadOnlyCollection<WorkflowSignatureField> SignatureFieldsOf(Block block)
    {
        Workflow? workflow = block.Section?.Workflow;
        return workflow is null ? [] : [.. workflow.SignatureFields];
    }

    private static string? WrittenCounterpartyKey(Block block)
    {
        if (!block.ShouldUpdateCounterpartyWithResult)
        {
            return null;
        }

        if (block.CounterpartyResultType != CounterpartyResultType.Property)
        {
            return nameof(Counterparty.LastFinancialAnalysisResult);
        }

        string? key = block.UpdateCounterpartyPropertyKey?.Trim();
        return string.IsNullOrEmpty(key) ? null : key;
    }

    private static string? CounterpartyKeyOf(string placeholder)
    {
        string[] parts = placeholder.Split('.');
        return parts.Length > 1 ? parts[1] : null;
    }

    private static bool IsLocalInput(Occurrence occurrence, string placeholder)
    {
        return occurrence.Scanned.TextInputs.Any(input =>
            string.Equals(input.ReplacementTag, placeholder, StringComparison.Ordinal));
    }

    /// <summary>
    /// The runtime injects a signature field only into a signature-bearing block whose properties
    /// carry the field's token, under the field's exact name, so only that exact placeholder is
    /// local; any other spelling still resolves from the blocks.
    /// </summary>
    private static bool IsSignatureField(Occurrence occurrence, string placeholder)
    {
        if (occurrence.Scanned.Properties is not IHasSignatureFields)
        {
            return false;
        }

        return occurrence.SignatureFields.Any(field =>
            !string.IsNullOrWhiteSpace(field.Name)
            && string.Equals(field.Name, placeholder, StringComparison.Ordinal)
            && occurrence.Scanned.Properties.HasPlaceholder(
                Workflow.SignatureFieldToken(field.Name), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Whether the head of a placeholder (the part before the first dot) names an entity. Only
    /// peer companies take an index, so <c>PeerCompanies[1]</c> is an entity tag and
    /// <c>Counterparty[1]</c> is not.
    /// </summary>
    private static bool IsEntityTag(string head)
    {
        return head is SharedInputKeys.COUNTERPARTY or SharedInputKeys.ORGANIZATION
            || head.StartsWith(SharedInputKeys.PEER_COMPANIES, StringComparison.Ordinal);
    }

    /// <summary>
    /// The tag the run looks a placeholder up by: the part before the first dot, index included.
    /// A block tag with an index (<c>{x[1]}</c>) names no block at run time, so it stays unresolved.
    /// </summary>
    private static string HeadOf(string placeholder)
    {
        return placeholder.Split('.')[0];
    }

    private static bool IsReservedTag(string baseTag, string placeholder)
    {
        return SharedInputKeys.ReservedReplacementTags.Contains(baseTag)
            || SharedInputKeys.ReservedReplacementTags.Contains(placeholder);
    }

    /// <summary>
    /// Only the monitoring handlers put these into the shared inputs, so in any other run a block
    /// carrying the tag is what supplies it.
    /// </summary>
    private static bool IsMonitoringOnlyTag(string baseTag)
    {
        return baseTag is SharedInputKeys.PARENT_COUNTERPARTY or SharedInputKeys.COUNTERPARTIES_LIST;
    }

    private void RegisterVariables(IEnumerable<WorkflowVariable> variables)
    {
        foreach (WorkflowVariable variable in variables)
        {
            if (string.IsNullOrWhiteSpace(variable.ReplacementTag))
            {
                continue;
            }

            DependencyInput input = Input(DependencyInputKind.Variable, variable.ReplacementTag);
            RegisterProducer(variable.ReplacementTag, input.Id, variantKey: null, new VariableSupply(variable.DefaultValue is not null));
        }
    }

    private void PlaceBlocks(List<(Block Block, int SectionOrder, IReadOnlyList<string> VariantKeys)> placed)
    {
        List<Block> allBlocks = [.. placed.Select(static entry => entry.Block)];
        Dictionary<string, string> groupByBlockId = CollectVerificationGroups(allBlocks);

        foreach ((Block block, int sectionOrder, IReadOnlyList<string> variantKeys) in placed)
        {
            var node = new DependencyBlock(
                block.Id,
                block,
                sectionOrder,
                variantKeys,
                groupByBlockId.GetValueOrDefault(block.Id));

            _blocks.Add(node);
            _blocksById[block.Id] = node;
        }
    }

    /// <summary>
    /// Stamps each verifier and every member it names with the verifier's tag (its id when
    /// untagged). Members resolve by block replacement tag only, so a member tag colliding with a
    /// variable's tag never pulls the variable in.
    /// </summary>
    private Dictionary<string, string> CollectVerificationGroups(List<Block> allBlocks)
    {
        Dictionary<string, string> groupByBlockId = new(StringComparer.Ordinal);
        List<DependencyVerificationGroup> groups = [];

        foreach (Block verifier in allBlocks.Where(static block => block.Type == BlockTypes.Verification))
        {
            if (verifier.Properties is not VerificationProperties props)
            {
                continue;
            }

            string key = string.IsNullOrWhiteSpace(verifier.ReplacementTag) ? verifier.Id : verifier.ReplacementTag;
            groupByBlockId[verifier.Id] = key;

            List<string> memberTags = [];
            List<string> memberIds = [];
            foreach (string memberTag in props.DeclaredMemberTags())
            {
                List<Block> members = allBlocks.CarryingTag(memberTag);
                if (members.Count == 0)
                {
                    continue;
                }

                memberTags.Add(memberTag);
                foreach (Block member in members)
                {
                    groupByBlockId[member.Id] = key;
                    memberIds.Add(member.Id);
                }
            }

            groups.Add(new DependencyVerificationGroup(key, verifier.Id, memberTags, memberIds));
        }

        _verificationGroups = groups;

        return groupByBlockId;
    }

    /// <summary>
    /// A plain section is scanned as authored. A parameterized section is scanned once per variant
    /// through the expander's own rewrite on an in-memory copy, and each variant's suffixed tag
    /// (<c>tag_KEY</c>) aliases the template block. The un-suffixed template tag is not a producer:
    /// after expansion no block carries it.
    /// </summary>
    private void AddSectionOccurrences(SectionScope scope, IReadOnlyCollection<WorkflowSignatureField> signatureFields)
    {
        (Section section, List<SectionVariant> variants, IReadOnlyList<string>? blockClasses) = scope;
        List<DependencyBlock> nodes = [.. section.Blocks
            .OrderBy(static block => block.Order)
            .Select(block => _blocksById[block.Id])];

        if (variants.Count == 0)
        {
            foreach (DependencyBlock node in nodes)
            {
                AddPlainOccurrence(node, signatureFields);
            }

            return;
        }

        IReadOnlyList<Func<string, string>> rewriters = ParameterizedSectionExpander.VariantRewriters(
            section,
            new ParameterizedSectionConfig(variants));

        for (int index = 0; index < variants.Count; index++)
        {
            SectionVariant variant = variants[index];
            HashSet<string> parameterNames = [.. variant.Params?.Keys ?? Enumerable.Empty<string>()];

            foreach (DependencyBlock node in nodes)
            {
                if (!string.IsNullOrWhiteSpace(node.Block.ReplacementTag))
                {
                    RegisterProducer($"{node.Block.ReplacementTag}_{variant.Key}", node.Id, variant.Key);
                }

                Block scanned = node.Block.CloneDetached();
                if (blockClasses is not null)
                {
                    scanned.SupportedDocumentClasses = [.. blockClasses];
                }

                ParameterizedSectionExpander.ApplyVariantToBlock(scanned, variant, rewriters[index]);
                _occurrences.Add(new Occurrence(node, scanned, variant.Key, signatureFields, parameterNames));
            }
        }
    }

    private void AddPlainOccurrence(DependencyBlock node, IReadOnlyCollection<WorkflowSignatureField> signatureFields)
    {
        RegisterProducer(node.Block.ReplacementTag, node.Id, variantKey: null);
        _occurrences.Add(new Occurrence(node, node.Block, VariantKey: null, signatureFields, _noParameters));
    }

    private void RegisterProducer(string? tag, string nodeId, string? variantKey, VariableSupply? variable = null)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return;
        }

        if (!_producersByTag.TryGetValue(tag, out List<Producer>? producers))
        {
            producers = [];
            _producersByTag[tag] = producers;
        }

        if (!producers.Any(producer => producer.NodeId == nodeId && producer.VariantKey == variantKey))
        {
            producers.Add(new Producer(nodeId, variantKey, variable));
        }
    }

    private void ResolveAll()
    {
        foreach (Occurrence occurrence in _occurrences)
        {
            if (occurrence.Scanned.Properties is LoopStartProperties { ListTitle: { } listTitle }
                && !string.IsNullOrWhiteSpace(listTitle))
            {
                _ = _loopTitles.Add(listTitle);
            }
        }

        foreach (Occurrence occurrence in _occurrences)
        {
            ResolveReads(occurrence);
        }

        AddDocumentEdges();
        AddCounterpartyEdges();
    }

    private void ResolveReads(Occurrence occurrence)
    {
        string readerId = occurrence.Node.Id;

        foreach (string placeholder in occurrence.Scanned.GetAllPlaceholders())
        {
            if (IsLocalInput(occurrence, placeholder) || occurrence.ParameterNames.Contains(placeholder))
            {
                continue;
            }

            if (IsSignatureField(occurrence, placeholder))
            {
                AddEdge(Input(DependencyInputKind.Signature, placeholder).Id, readerId,
                    DependencyEdgeKind.Data, placeholder, occurrence.VariantKey);
                continue;
            }

            string head = HeadOf(placeholder);
            if (string.IsNullOrWhiteSpace(head))
            {
                continue;
            }

            if (IsEntityTag(head))
            {
                if (head == SharedInputKeys.COUNTERPARTY)
                {
                    _counterpartyReads.Add((occurrence, placeholder));
                }

                AddEdge(Input(DependencyInputKind.System, head.GetBaseReplacementTag()).Id, readerId,
                    DependencyEdgeKind.Data, placeholder, occurrence.VariantKey);
                continue;
            }

            foreach ((string sourceId, string? variantKey) in ResolveTag(head, placeholder, readerId, occurrence.VariantKey, recordRead: true))
            {
                AddEdge(sourceId, readerId, DependencyEdgeKind.Data, placeholder, variantKey ?? occurrence.VariantKey);
                AddOrderingLink(sourceId, variantKey, occurrence);
            }
        }
    }

    /// <summary>
    /// The sources of a base tag that is not block-local: a reserved input the run always injects,
    /// else the blocks and variables carrying it that the run would read (see
    /// <see cref="SupplyingProducers"/>), else a block addressed by its id, else a monitoring-only
    /// reserved input or a loop input, else an unresolved input. A block reading its own tag
    /// resolves to itself and yields no source.
    /// </summary>
    private List<(string SourceId, string? VariantKey)> ResolveTag(
        string baseTag,
        string placeholder,
        string readerId,
        string? readerVariant,
        bool recordRead)
    {
        bool reserved = IsReservedTag(baseTag, placeholder);
        if (reserved && !IsMonitoringOnlyTag(baseTag))
        {
            return [(Input(DependencyInputKind.System, baseTag).Id, null)];
        }

        if (_producersByTag.TryGetValue(baseTag, out List<Producer>? producers))
        {
            List<Producer> supplying = SupplyingProducers(producers, placeholder == baseTag);
            if (supplying.All(static producer => producer.Variable is not null))
            {
                return [.. supplying.Select(static producer => (producer.NodeId, producer.VariantKey))];
            }

            if (recordRead)
            {
                _tagReads.Add(new DependencyTagRead(readerId, baseTag, placeholder));
            }

            return [.. supplying
                .Where(producer => producer.NodeId != readerId || producer.VariantKey != readerVariant)
                .Select(static producer => (producer.NodeId, producer.VariantKey))];
        }

        if (_blocksById.ContainsKey(baseTag))
        {
            return baseTag == readerId ? [] : [(baseTag, null)];
        }

        DependencyInputKind kind = reserved ? DependencyInputKind.System
            : _loopTitles.Contains(baseTag) ? DependencyInputKind.Loop
            : DependencyInputKind.Unresolved;

        if (kind == DependencyInputKind.Unresolved)
        {
            if (!_unresolvedReaders.TryGetValue(baseTag, out List<string>? readers))
            {
                readers = [];
                _unresolvedReaders[baseTag] = readers;
            }

            if (!readers.Contains(readerId))
            {
                readers.Add(readerId);
            }
        }

        return [(Input(kind, baseTag).Id, null)];
    }

    /// <summary>
    /// A producer's documents attach to a reader only after the producer runs, so every reader of
    /// the class waits for it whatever their authored order: a reader authored first would
    /// otherwise always see none. A reader the run dispatches in an earlier segment cannot wait,
    /// so it is an issue rather than an edge. The rule itself is <see cref="Block.ReadsDocumentsCreatedBy"/>,
    /// shared with <see cref="Workflow.IsolateBlockWithDependencies"/>.
    /// </summary>
    private void AddDocumentEdges()
    {
        foreach (Occurrence producer in _occurrences)
        {
            if (producer.Scanned.GetCreatedDocumentClass() is not { } documentClass)
            {
                continue;
            }

            foreach (Occurrence reader in _occurrences)
            {
                if (reader.Node.Id == producer.Node.Id || !reader.Scanned.ReadsDocumentsCreatedBy(producer.Scanned))
                {
                    continue;
                }

                if (DispatchedBefore(reader, producer))
                {
                    AddOrderingIssue(DependencyIssueKind.ReadsDocumentsBeforeProducer, reader, producer, documentClass);
                    continue;
                }

                AddEdge(producer.Node.Id, reader.Node.Id, DependencyEdgeKind.Document, documentClass,
                    producer.VariantKey ?? reader.VariantKey);
                AddOrderingLink(producer.Node.Id, producer.VariantKey, reader);
            }
        }
    }

    /// <summary>
    /// A write-back reaches only the readers of the key it writes, whatever their authored order.
    /// <c>{Counterparty}</c> and <c>{Counterparty.Name}</c> read the name, which no write-back
    /// changes. A reader that already feeds the writer reads the stored value, so it gains no
    /// edge and no cycle is reported. A reader the run dispatches in an earlier segment cannot
    /// wait, so it is an issue rather than an edge.
    /// </summary>
    private void AddCounterpartyEdges()
    {
        Dictionary<string, List<string>> successors = _blocks.ToDictionary(
            static block => block.Id,
            static _ => new List<string>(),
            StringComparer.Ordinal);

        foreach (EdgeAccumulator edge in _edges)
        {
            if (successors.TryGetValue(edge.SourceId, out List<string>? next) && successors.ContainsKey(edge.TargetId))
            {
                next.Add(edge.TargetId);
            }
        }

        foreach (Occurrence writer in _occurrences)
        {
            if (WrittenCounterpartyKey(writer.Scanned) is not { } writtenKey)
            {
                continue;
            }

            foreach ((Occurrence reader, string placeholder) in _counterpartyReads)
            {
                if (reader.Node.Id == writer.Node.Id
                    || !string.Equals(CounterpartyKeyOf(placeholder), writtenKey, StringComparison.OrdinalIgnoreCase)
                    || GraphReachability.Reaches(successors, reader.Node.Id, writer.Node.Id))
                {
                    continue;
                }

                if (DispatchedBefore(reader, writer))
                {
                    AddOrderingIssue(DependencyIssueKind.ReadsCounterpartyBeforeWriter, reader, writer, placeholder);
                    continue;
                }

                AddEdge(writer.Node.Id, reader.Node.Id, DependencyEdgeKind.Counterparty, placeholder,
                    writer.VariantKey ?? reader.VariantKey);
                AddOrderingLink(writer.Node.Id, writer.VariantKey, reader);

                if (!successors[writer.Node.Id].Contains(reader.Node.Id))
                {
                    successors[writer.Node.Id].Add(reader.Node.Id);
                }
            }
        }
    }

    /// <summary>
    /// The run puts variables into the shared inputs under their exact tag, and a shared input wins
    /// over a block for that exact placeholder. So a variable with a default hides a block carrying
    /// the tag from <c>{tag}</c>, and a block hides every variable from <c>{tag.part}</c>. A
    /// variable with no default is in the shared inputs only when the run is given a value, so
    /// either may supply <c>{tag}</c>.
    /// </summary>
    private static List<Producer> SupplyingProducers(List<Producer> producers, bool exactTag)
    {
        if (exactTag && producers.Any(static producer => producer.Variable is { HasDefault: true }))
        {
            return [.. producers.Where(static producer => producer.Variable is { HasDefault: true })];
        }

        if (!exactTag && producers.Any(static producer => producer.Variable is null))
        {
            return [.. producers.Where(static producer => producer.Variable is null)];
        }

        return producers;
    }

    private bool DispatchedBefore(Occurrence reader, Occurrence producer)
    {
        return _segmentOf is not null
            && _segmentOf.TryGetValue(new OrderingNode(reader.Node.Id, reader.VariantKey), out int readerSegment)
            && _segmentOf.TryGetValue(new OrderingNode(producer.Node.Id, producer.VariantKey), out int producerSegment)
            && readerSegment < producerSegment;
    }

    private void AddOrderingIssue(DependencyIssueKind kind, Occurrence reader, Occurrence producer, string tag)
    {
        if (_orderingIssueKeys.Add((kind, reader.Node.Id, producer.Node.Id, tag)))
        {
            _orderingIssues.Add(new DependencyIssue(kind, [reader.Node.Id, producer.Node.Id], tag));
        }
    }

    private void AddOrderingLink(string sourceId, string? sourceVariant, Occurrence reader)
    {
        if (_blocksById.ContainsKey(sourceId))
        {
            _orderingLinks.Add(new OrderingLink(
                new OrderingNode(sourceId, sourceVariant),
                new OrderingNode(reader.Node.Id, reader.VariantKey)));
        }
    }

    /// <summary>
    /// A section gate reads its tags before any of the section's blocks run, so each source of a
    /// gate tag feeds every block of the section. A parameterized section is gated per variant.
    /// </summary>
    private void AddGateEdges(Section section, List<SectionVariant> variants)
    {
        List<(string? VariantKey, string? Gate)> gates = variants.Count == 0
            ? [(null, section.TextInputTemplate)]
            : [.. variants.Select(static variant => ((string?)variant.Key, variant.SectionStopGate))];

        List<string> targets = [.. section.Blocks.OrderBy(static block => block.Order).Select(static block => block.Id)];

        foreach ((string? variantKey, string? gate) in gates)
        {
            if (string.IsNullOrWhiteSpace(gate))
            {
                continue;
            }

            foreach (string placeholder in gate.GetPlaceholders().Distinct())
            {
                string head = HeadOf(placeholder);
                if (string.IsNullOrWhiteSpace(head))
                {
                    continue;
                }

                foreach (string target in targets)
                {
                    List<(string SourceId, string? VariantKey)> sources = IsEntityTag(head)
                        ? [(Input(DependencyInputKind.System, head.GetBaseReplacementTag()).Id, null)]
                        : ResolveTag(head, placeholder, target, variantKey, recordRead: false);

                    foreach ((string sourceId, string? sourceVariant) in sources)
                    {
                        AddEdge(sourceId, target, DependencyEdgeKind.Gate, placeholder, sourceVariant ?? variantKey);
                    }
                }
            }
        }
    }

    private DependencyInput Input(DependencyInputKind kind, string tag)
    {
        string prefix = kind switch
        {
            DependencyInputKind.Variable => VARIABLE_PREFIX,
            DependencyInputKind.Signature => SIGNATURE_PREFIX,
            DependencyInputKind.System => SYSTEM_PREFIX,
            DependencyInputKind.Loop => LOOP_PREFIX,
            _ => UNRESOLVED_PREFIX,
        };

        string id = prefix + tag;
        if (!_inputsById.TryGetValue(id, out DependencyInput? input))
        {
            input = new DependencyInput(id, kind, tag);
            _inputsById[id] = input;
            _inputs.Add(input);
        }

        return input;
    }

    private void AddEdge(string sourceId, string targetId, DependencyEdgeKind kind, string tag, string? variantKey)
    {
        if (sourceId == targetId)
        {
            return;
        }

        if (!_edgesByKey.TryGetValue((sourceId, targetId, kind), out EdgeAccumulator? edge))
        {
            edge = new EdgeAccumulator(sourceId, targetId, kind);
            _edgesByKey[(sourceId, targetId, kind)] = edge;
            _edges.Add(edge);
        }

        if (!edge.Tags.Contains(tag))
        {
            edge.Tags.Add(tag);
        }

        if (variantKey is not null && !edge.VariantKeys.Contains(variantKey))
        {
            edge.VariantKeys.Add(variantKey);
        }
    }

    private WorkflowDependencyMap Freeze()
    {
        List<DependencyEdge> edges = [.. _edges.Select(static edge => new DependencyEdge(
            edge.SourceId, edge.TargetId, edge.Kind, edge.Tags, edge.VariantKeys))];

        List<DependencyIssue> issues = [.. _unresolvedReaders.Select(static entry =>
            new DependencyIssue(DependencyIssueKind.UnresolvedTag, entry.Value, entry.Key))];
        issues.AddRange(DependencyCycles.Find(_blocks, _orderingLinks)
            .Select(static cycle => new DependencyIssue(DependencyIssueKind.Cycle, cycle, Tag: null)));
        issues.AddRange(_orderingIssues);

        return new WorkflowDependencyMap(_blocks, _inputs, edges, _verificationGroups, issues, _tagReads);
    }

    /// <summary>
    /// One scan of a block: the block itself, or one variant's in-memory rewrite of it. A
    /// parameter name still standing after the rewrite sits in a field the expander leaves as
    /// written (a prompt preview), so it names a parameter, not a missing tag.
    /// </summary>
    private sealed record Occurrence(
        DependencyBlock Node,
        Block Scanned,
        string? VariantKey,
        IReadOnlyCollection<WorkflowSignatureField> SignatureFields,
        IReadOnlySet<string> ParameterNames);

    /// <param name="Variable">Set when the producer is a workflow variable rather than a block.</param>
    private sealed record Producer(string NodeId, string? VariantKey, VariableSupply? Variable);

    /// <param name="HasDefault">The run always puts a variable with a default into the shared inputs.</param>
    private sealed record VariableSupply(bool HasDefault);

    /// <param name="BlockClasses">The class list the run's normalization gives every block, or null when they keep their own.</param>
    private sealed record SectionScope(Section Section, List<SectionVariant> Variants, IReadOnlyList<string>? BlockClasses);

    private sealed class EdgeAccumulator(string sourceId, string targetId, DependencyEdgeKind kind)
    {
        public string SourceId { get; } = sourceId;
        public string TargetId { get; } = targetId;
        public DependencyEdgeKind Kind { get; } = kind;
        public List<string> Tags { get; } = [];
        public List<string> VariantKeys { get; } = [];
    }
}

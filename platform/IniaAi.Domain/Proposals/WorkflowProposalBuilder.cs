using System.Globalization;
using System.Text.RegularExpressions;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.Domain.Proposals;

/// <summary>
/// Builds a <see cref="WorkflowProposal"/> from the workflow an agent was given and the workflow it
/// wrote back. Blocks pair by StableBlockId and sections by StableSectionId, never by tag, so a
/// renamed or moved block stays the same block. Each changed field becomes one change carrying its
/// base value. Row ids, stamps and run evidence are not content and never make a change.
/// </summary>
public static partial class WorkflowProposalBuilder
{
    public const string NEW_SECTION_KEY_PREFIX = "new-section-";
    public const string NEW_BLOCK_KEY_PREFIX = "new-block-";

    private const string SECTION_GROUP = "section";
    private const string BLOCK_GROUP = "block";
    private const string ID_KEY_PREFIX = "id:";
    private const string BASE_KEY_PREFIX = "base-";
    private const string HASHED_KEY_PREFIX = "sha256-";
    private const int MAX_FIELD_IN_ID = 200;

    /// <summary>The longest change id: a decision row's key column.</summary>
    public const int MAX_CHANGE_ID_LENGTH = 450;
    private const char ID_SEPARATOR = ':';

    private static readonly HashSet<string> _outboundTypes = new(StringComparer.Ordinal)
    {
        nameof(BlockTypes.WebApiCall),
        nameof(BlockTypes.IntelligentSearch),
        nameof(BlockTypes.ExchangeRate),
        nameof(BlockTypes.PeerFinancials),
    };

    /// <summary>The block types that call an outside service when they run.</summary>
    public static IReadOnlySet<string> OutboundBlockTypes => _outboundTypes;

    private static readonly HashSet<string> _workflowSkippedKeys = new(StringComparer.Ordinal)
    {
        ProposalJson.SECTIONS, ProposalJson.VARIABLES, ProposalJson.DOCUMENT_CLASSES,
        ProposalJson.SIGNATURE_FIELDS, ProposalJson.REGRESSION_FIELDS,
        "FormatVersion", "CitationTracePages", "WorkflowType", "Children",
    };

    private static readonly (ProposedChangeScope Scope, string Collection, string KeyField)[] _keyedCollections =
    [
        (ProposedChangeScope.Variable, ProposalJson.VARIABLES, ProposalJson.TAG),
        (ProposedChangeScope.DocumentClass, ProposalJson.DOCUMENT_CLASSES, ProposalJson.KEY),
        (ProposedChangeScope.SignatureField, ProposalJson.SIGNATURE_FIELDS, ProposalJson.NAME),
        (ProposedChangeScope.RegressionField, ProposalJson.REGRESSION_FIELDS, ProposalJson.NAME),
    ];

    /// <summary>Reads JSON text as the builder does, with dates and decimals left as written.</summary>
    public static JObject Parse(string json)
    {
        return ProposalJson.ParseObject(json);
    }

    public static WorkflowProposal Build(string baseJson, string proposedJson, IReadOnlyList<ProposalSummaryEntry> summary)
    {
        return Build(Parse(baseJson), Parse(proposedJson), summary);
    }

    /// <summary>Reads both roots without changing them, so a caller can reuse them.</summary>
    public static WorkflowProposal Build(JObject baseRoot, JObject proposedRoot, IReadOnlyList<ProposalSummaryEntry> summary)
    {
        var changes = new List<ProposedChange>();

        Tree baseTree = Tree.Of(baseRoot, isBase: true);
        Tree proposedTree = Tree.Of(proposedRoot, isBase: false);
        var index = new SummaryIndex(summary);

        AddSectionChanges(baseTree, proposedTree, index, changes);
        AddBlockChanges(baseTree, proposedTree, index, changes);
        MarkRenumbering(baseTree, proposedTree, changes);

        foreach ((ProposedChangeScope scope, string collection, string keyField) in _keyedCollections)
        {
            AddItemChanges(scope, baseRoot[collection] as JArray, proposedRoot[collection] as JArray, keyField, changes);
        }

        AddWorkflowFieldChanges(baseRoot, proposedRoot, changes);

        return new WorkflowProposal { Changes = changes, Summary = [.. summary] };
    }

    /// <summary>
    /// Attaches the checks' errors and refuses the changes they trace to: a path into the proposed
    /// workflow or a quoted block tag names the block; an error that names no block refuses every
    /// change, because nothing shows which change caused it.
    /// </summary>
    public static void AttachCheckErrors(WorkflowProposal proposal, string proposedJson, IReadOnlyList<ProposalCheckError> errors)
    {
        JObject? proposedRoot;
        try
        {
            proposedRoot = Parse(proposedJson);
        }
        catch (JsonException)
        {
            proposedRoot = null;
        }

        AttachCheckErrors(proposal, proposedRoot, errors);
    }

    /// <param name="proposedRoot">The proposed workflow, or null when it could not be read.</param>
    public static void AttachCheckErrors(WorkflowProposal proposal, JObject? proposedRoot, IReadOnlyList<ProposalCheckError> errors)
    {
        if (errors.Count == 0)
        {
            return;
        }

        Tree? tree = proposedRoot is null ? null : Tree.Of(proposedRoot, isBase: false);

        Dictionary<string, List<ProposedChange>> byBlock = proposal.Changes
            .Where(static change => change.Scope == ProposedChangeScope.Block && change.TargetKey is not null)
            .GroupBy(static change => change.TargetKey!, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToList(), StringComparer.Ordinal);
        Dictionary<string, ProposedChange> addedSections = proposal.Changes
            .Where(static change => change is { Scope: ProposedChangeScope.Section, Kind: ProposedChangeKind.Added })
            .ToDictionary(static change => change.TargetKey!, StringComparer.Ordinal);
        Dictionary<string, List<string>> keysByTag = (tree?.Blocks ?? [])
            .Select(static block => (block.Key, Tag: ProposalJson.StringOf(block.Json[ProposalJson.TAG])))
            .Where(static pair => !string.IsNullOrEmpty(pair.Tag))
            .GroupBy(static pair => pair.Tag!, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Select(static pair => pair.Key).ToList(), StringComparer.Ordinal);

        foreach (ProposalCheckError error in errors)
        {
            List<string> blockKeys = error.BlockKey is string named
                ? [named]
                : BlockKeyFromPath(tree, error.Message) is string pathKey
                    ? [pathKey]
                    : [.. keysByTag.Where(pair => MentionsTag(error.Message, pair.Key)).SelectMany(static pair => pair.Value)];

            proposal.CheckErrors.Add(error with { BlockKey = blockKeys.Count == 1 ? blockKeys[0] : error.BlockKey });

            IEnumerable<ProposedChange> refused = blockKeys.Count == 0
                ? proposal.Changes
                : blockKeys.SelectMany(key => ChangesOfBlock(key, tree, byBlock, addedSections));
            foreach (ProposedChange change in refused.Where(static change => change.Refusal is null))
            {
                change.Refusal = ProposalRefusal.FailedCheck;
                change.RefusedReason = error.Message;
            }
        }
    }

    /// <summary>A block's own changes, or the added section that carries it when it is new with its section.</summary>
    private static IEnumerable<ProposedChange> ChangesOfBlock(
        string key,
        Tree? tree,
        Dictionary<string, List<ProposedChange>> byBlock,
        Dictionary<string, ProposedChange> addedSections)
    {
        if (byBlock.TryGetValue(key, out List<ProposedChange>? own))
        {
            return own;
        }

        return tree is not null
            && tree.BlocksByKey.TryGetValue(key, out BlockNode? block)
            && addedSections.TryGetValue(block.SectionKey, out ProposedChange? section)
                ? [section]
                : [];
    }

    private static void AddSectionChanges(
        Tree baseTree,
        Tree proposedTree,
        SummaryIndex summary,
        List<ProposedChange> changes)
    {
        foreach (SectionNode section in proposedTree.Sections)
        {
            string title = SectionTitle(section.Json);
            bool missing = !summary.HasSection(section.Key, title);

            if (!baseTree.SectionsByKey.TryGetValue(section.Key, out SectionNode? before))
            {
                JObject added = SectionWithBlocks(section, proposedTree, keep: block => !baseTree.BlocksByKey.ContainsKey(block.Key));
                changes.Add(new ProposedChange
                {
                    Id = ChangeId(ProposedChangeScope.Section, section.Key, nameof(ProposedChangeKind.Added)),
                    Scope = ProposedChangeScope.Section,
                    Kind = ProposedChangeKind.Added,
                    TargetKey = section.Key,
                    NewJson = added.ToString(Formatting.None),
                    Name = title,
                    SendsDataOutside = proposedTree.Blocks.Any(block => block.SectionKey == section.Key
                        && !baseTree.BlocksByKey.ContainsKey(block.Key)
                        && IsOutbound(BlockTypeOf(block.Json))),
                    MissingFromSummary = missing,
                    Refusal = HasLinkFieldsAnywhere(added) ? ProposalRefusal.LibraryLink : null,
                });
                continue;
            }

            bool linked = IsLinked(before.Json) || IsLinked(section.Json);
            foreach ((string field, JToken? baseValue, JToken? newValue) in DiffFields(before.Json, section.Json, SectionFieldSkipped, expandProperties: false))
            {
                changes.Add(new ProposedChange
                {
                    Id = ChangeId(ProposedChangeScope.Section, section.Key, field),
                    Scope = ProposedChangeScope.Section,
                    Kind = ProposedChangeKind.FieldEdited,
                    TargetKey = section.Key,
                    Field = field,
                    BaseJson = ProposalJson.Text(baseValue),
                    NewJson = ProposalJson.Text(newValue),
                    Name = title,
                    LibraryLinked = linked,
                    MissingFromSummary = missing && field != ProposalJson.ORDER,
                    Refusal = ProposalJson.IsSharedComponentKey(field) ? ProposalRefusal.LibraryLink : null,
                });
            }
        }

        foreach (SectionNode section in baseTree.Sections.Where(section => !proposedTree.SectionsByKey.ContainsKey(section.Key)))
        {
            string title = SectionTitle(section.Json);
            JObject removed = SectionWithBlocks(section, baseTree, keep: block => !proposedTree.BlocksByKey.ContainsKey(block.Key));
            changes.Add(new ProposedChange
            {
                Id = ChangeId(ProposedChangeScope.Section, section.Key, nameof(ProposedChangeKind.Removed)),
                Scope = ProposedChangeScope.Section,
                Kind = ProposedChangeKind.Removed,
                TargetKey = section.Key,
                BaseJson = removed.ToString(Formatting.None),
                Name = title,
                LibraryLinked = IsLinked(section.Json),
                MissingFromSummary = !summary.HasSection(section.Key, title),
            });
        }
    }

    private static void AddBlockChanges(
        Tree baseTree,
        Tree proposedTree,
        SummaryIndex summary,
        List<ProposedChange> changes)
    {
        foreach (BlockNode block in proposedTree.Blocks)
        {
            string? type = BlockTypeOf(block.Json);
            string? tag = ProposalJson.StringOf(block.Json[ProposalJson.TAG]);
            string? name = ProposalJson.StringOf(block.Json[ProposalJson.NAME]);
            var identity = new BlockIdentity(name, tag, type);
            bool missing = !summary.HasBlock(block.Key, tag);

            if (!baseTree.BlocksByKey.TryGetValue(block.Key, out BlockNode? before))
            {
                if (!baseTree.SectionsByKey.ContainsKey(block.SectionKey))
                {
                    continue;
                }

                JToken added = ProposalJson.Without(block.Json, ProposalJson.EvidenceKeys);
                changes.Add(new ProposedChange
                {
                    Id = ChangeId(ProposedChangeScope.Block, block.Key, nameof(ProposedChangeKind.Added)),
                    Scope = ProposedChangeScope.Block,
                    Kind = ProposedChangeKind.Added,
                    TargetKey = block.Key,
                    SectionKey = block.SectionKey,
                    NewJson = added.ToString(Formatting.None),
                    Name = name,
                    Tag = tag,
                    BlockType = type,
                    SendsDataOutside = IsOutbound(type),
                    LibraryLinked = proposedTree.SectionsByKey.TryGetValue(block.SectionKey, out SectionNode? into) && IsLinked(into.Json),
                    MissingFromSummary = missing,
                    Refusal = ProposalJson.HasSharedComponentFields(block.Json) ? ProposalRefusal.LibraryLink : null,
                });
                continue;
            }

            string? baseType = BlockTypeOf(before.Json);
            bool linked = IsLinked(before.Json) || IsLinked(block.Json) || before.SectionLinked || block.SectionLinked;
            bool outbound = IsOutbound(type) || IsOutbound(baseType);

            if (before.SectionKey != block.SectionKey)
            {
                changes.Add(BlockChange(block, identity, ProposedChangeKind.Moved, field: null, JValue.CreateString(before.SectionKey), JValue.CreateString(block.SectionKey)) with
                {
                    LibraryLinked = linked,
                    MissingFromSummary = missing,
                });
            }

            bool typeChanged = !string.Equals(type, baseType, StringComparison.Ordinal);
            if (typeChanged)
            {
                changes.Add(BlockChange(block, identity, ProposedChangeKind.FieldEdited, ProposalJson.TYPE, TypeAndProperties(before.Json), TypeAndProperties(block.Json)) with
                {
                    SendsDataOutside = outbound,
                    LibraryLinked = linked,
                    MissingFromSummary = missing,
                });
            }

            foreach ((string field, JToken? baseValue, JToken? newValue) in DiffFields(before.Json, block.Json, BlockFieldSkipped, expandProperties: !typeChanged))
            {
                if (typeChanged && field is ProposalJson.PROPERTIES or ProposalJson.TYPE)
                {
                    continue;
                }

                changes.Add(BlockChange(block, identity, ProposedChangeKind.FieldEdited, field, baseValue, newValue) with
                {
                    SendsDataOutside = outbound && field.StartsWith(ProposalJson.PROPERTIES_PREFIX, StringComparison.Ordinal),
                    LibraryLinked = linked,
                    MissingFromSummary = missing && field != ProposalJson.ORDER,
                    Refusal = ProposalJson.IsSharedComponentKey(field) ? ProposalRefusal.LibraryLink : null,
                });
            }
        }

        foreach (BlockNode block in baseTree.Blocks.Where(block => !proposedTree.BlocksByKey.ContainsKey(block.Key)))
        {
            if (!proposedTree.SectionsByKey.ContainsKey(block.SectionKey))
            {
                continue;
            }

            string? tag = ProposalJson.StringOf(block.Json[ProposalJson.TAG]);
            changes.Add(new ProposedChange
            {
                Id = ChangeId(ProposedChangeScope.Block, block.Key, nameof(ProposedChangeKind.Removed)),
                Scope = ProposedChangeScope.Block,
                Kind = ProposedChangeKind.Removed,
                TargetKey = block.Key,
                SectionKey = block.SectionKey,
                BaseJson = ProposalJson.Text(ProposalJson.Without(block.Json, ProposalJson.EvidenceKeys)),
                Name = ProposalJson.StringOf(block.Json[ProposalJson.NAME]),
                Tag = tag,
                BlockType = BlockTypeOf(block.Json),
                LibraryLinked = IsLinked(block.Json) || block.SectionLinked,
                MissingFromSummary = !summary.HasBlock(block.Key, tag),
            });
        }
    }

    /// <summary>
    /// Marks the <c>Order</c> edits of a list as <see cref="ProposedChange.Incidental"/> when the
    /// items the list holds on both sides keep their order: the numbers only shifted around an
    /// item added, removed or moved. A list whose kept items changed order is a reorder the user
    /// reviews, so its edits stay.
    /// </summary>
    private static void MarkRenumbering(Tree baseTree, Tree proposedTree, List<ProposedChange> changes)
    {
        bool sectionsKeepOrder = KeptInOrder(baseTree.Sections.Select(static (section, i) => (section.Key, section.Json, i)), proposedTree.SectionsByKey.ContainsKey)
            .SequenceEqual(KeptInOrder(proposedTree.Sections.Select(static (section, i) => (section.Key, section.Json, i)), baseTree.SectionsByKey.ContainsKey), StringComparer.Ordinal);

        var blocksKeepOrder = new HashSet<string>(StringComparer.Ordinal);
        foreach (SectionNode section in proposedTree.Sections)
        {
            bool StayedHere(string key, Tree other) =>
                other.BlocksByKey.TryGetValue(key, out BlockNode? there) && there.SectionKey == section.Key;

            List<string> before = KeptInOrder(
                baseTree.Blocks.Where(block => block.SectionKey == section.Key).Select(static (block, i) => (block.Key, block.Json, i)),
                key => StayedHere(key, proposedTree));
            List<string> after = KeptInOrder(
                proposedTree.Blocks.Where(block => block.SectionKey == section.Key).Select(static (block, i) => (block.Key, block.Json, i)),
                key => StayedHere(key, baseTree));
            if (before.SequenceEqual(after, StringComparer.Ordinal))
            {
                _ = blocksKeepOrder.Add(section.Key);
            }
        }

        foreach (ProposedChange change in changes.Where(static change => change is { Kind: ProposedChangeKind.FieldEdited, Field: ProposalJson.ORDER }))
        {
            change.Incidental = change.Scope switch
            {
                ProposedChangeScope.Section => sectionsKeepOrder,
                ProposedChangeScope.Block => change.SectionKey is string section && blocksKeepOrder.Contains(section),
                _ => false,
            };
        }
    }

    /// <summary>The keys the other side also holds, in execution order: by <c>Order</c>, then by position.</summary>
    private static List<string> KeptInOrder(IEnumerable<(string Key, JObject Json, int Index)> nodes, Func<string, bool> kept)
    {
        return [.. nodes
            .Where(node => kept(node.Key))
            .OrderBy(static node => node.Json[ProposalJson.ORDER]?.Type == JTokenType.Integer ? node.Json[ProposalJson.ORDER]!.Value<long>() : long.MaxValue)
            .ThenBy(static node => node.Index)
            .Select(static node => node.Key)];
    }

    private static void AddItemChanges(
        ProposedChangeScope scope,
        JArray? baseItems,
        JArray? proposedItems,
        string keyField,
        List<ProposedChange> changes)
    {
        Dictionary<string, JObject> before = ItemsByKey(baseItems, keyField);
        Dictionary<string, JObject> after = ItemsByKey(proposedItems, keyField);

        foreach ((string key, JObject item) in after)
        {
            if (!before.TryGetValue(key, out JObject? old))
            {
                changes.Add(new ProposedChange
                {
                    Id = ChangeId(scope, key, nameof(ProposedChangeKind.Added)),
                    Scope = scope,
                    Kind = ProposedChangeKind.Added,
                    TargetKey = key,
                    NewJson = ProposalJson.Text(item),
                    Name = key,
                });
                continue;
            }

            foreach ((string field, JToken? baseValue, JToken? newValue) in DiffFields(old, item, static _ => false, expandProperties: false))
            {
                changes.Add(new ProposedChange
                {
                    Id = ChangeId(scope, key, field),
                    Scope = scope,
                    Kind = ProposedChangeKind.FieldEdited,
                    TargetKey = key,
                    Field = field,
                    BaseJson = ProposalJson.Text(baseValue),
                    NewJson = ProposalJson.Text(newValue),
                    Name = key,
                });
            }
        }

        foreach ((string key, JObject item) in before.Where(pair => !after.ContainsKey(pair.Key)))
        {
            changes.Add(new ProposedChange
            {
                Id = ChangeId(scope, key, nameof(ProposedChangeKind.Removed)),
                Scope = scope,
                Kind = ProposedChangeKind.Removed,
                TargetKey = key,
                BaseJson = ProposalJson.Text(item),
                Name = key,
            });
        }
    }

    private static void AddWorkflowFieldChanges(JObject baseRoot, JObject proposedRoot, List<ProposedChange> changes)
    {
        foreach ((string field, JToken? baseValue, JToken? newValue) in DiffFields(
            baseRoot, proposedRoot, key => _workflowSkippedKeys.Contains(key), expandProperties: false))
        {
            changes.Add(new ProposedChange
            {
                Id = ChangeId(ProposedChangeScope.Workflow, null, field),
                Scope = ProposedChangeScope.Workflow,
                Kind = ProposedChangeKind.FieldEdited,
                Field = field,
                BaseJson = ProposalJson.Text(baseValue),
                NewJson = ProposalJson.Text(newValue),
            });
        }
    }

    /// <summary>
    /// Every field whose value differs, noise aside. With <paramref name="expandProperties"/>, a
    /// block's <c>Properties</c> compares member by member, each its own field.
    /// </summary>
    private static IEnumerable<(string Field, JToken? Base, JToken? New)> DiffFields(
        JObject before,
        JObject after,
        Func<string, bool> skipped,
        bool expandProperties)
    {
        IEnumerable<string> names = before.Properties().Select(static p => p.Name)
            .Union(after.Properties().Select(static p => p.Name), StringComparer.Ordinal);

        foreach (string name in names)
        {
            if (skipped(name) || ProposalJson.NoiseKeys.Contains(name))
            {
                continue;
            }

            if (expandProperties && name == ProposalJson.PROPERTIES)
            {
                foreach ((string field, JToken? baseValue, JToken? newValue) in DiffFields(
                    before[name] as JObject ?? new JObject(), after[name] as JObject ?? new JObject(), static key => key == ProposalJson.TYPE, expandProperties: false))
                {
                    yield return (ProposalJson.PROPERTIES_PREFIX + field, baseValue, newValue);
                }

                continue;
            }

            JToken? baseValue2 = before[name];
            JToken? newValue2 = after[name];
            if (!ProposalJson.Same(baseValue2, newValue2))
            {
                yield return (name, baseValue2, newValue2);
            }
        }
    }

    private static bool BlockFieldSkipped(string key)
    {
        return key == ProposalJson.STABLE_BLOCK_ID || ProposalJson.EvidenceKeys.Contains(key);
    }

    private static bool SectionFieldSkipped(string key)
    {
        return key is ProposalJson.BLOCKS or ProposalJson.STABLE_SECTION_ID || ProposalJson.EvidenceKeys.Contains(key);
    }

    private static ProposedChange BlockChange(
        BlockNode block,
        BlockIdentity identity,
        ProposedChangeKind kind,
        string? field,
        JToken? baseValue,
        JToken? newValue)
    {
        return new ProposedChange
        {
            Id = ChangeId(ProposedChangeScope.Block, block.Key, field ?? kind.ToString()),
            Scope = ProposedChangeScope.Block,
            Kind = kind,
            TargetKey = block.Key,
            SectionKey = block.SectionKey,
            Field = field,
            BaseJson = ProposalJson.Text(baseValue),
            NewJson = ProposalJson.Text(newValue),
            Name = identity.Name,
            Tag = identity.Tag,
            BlockType = identity.BlockType,
        };
    }

    private static JObject TypeAndProperties(JObject block)
    {
        return new JObject
        {
            [ProposalJson.TYPE] = block[ProposalJson.TYPE]?.DeepClone(),
            [ProposalJson.PROPERTIES] = block[ProposalJson.PROPERTIES]?.DeepClone(),
        };
    }

    private static JObject SectionWithBlocks(SectionNode section, Tree tree, Func<BlockNode, bool> keep)
    {
        var json = (JObject)ProposalJson.Without(section.Json, ProposalJson.EvidenceKeys);
        json[ProposalJson.BLOCKS] = new JArray(tree.Blocks
            .Where(block => block.SectionKey == section.Key && keep(block))
            .Select(static block => ProposalJson.Without(block.Json, ProposalJson.EvidenceKeys)));

        return (JObject)ProposalJson.Normalize(json)!;
    }

    private static bool HasLinkFieldsAnywhere(JObject section)
    {
        return ProposalJson.HasSharedComponentFields(section)
            || (section[ProposalJson.BLOCKS] as JArray ?? new JArray()).OfType<JObject>().Any(ProposalJson.HasSharedComponentFields);
    }

    private static Dictionary<string, JObject> ItemsByKey(JArray? items, string keyField)
    {
        var byKey = new Dictionary<string, JObject>(StringComparer.Ordinal);
        foreach (JObject item in (items ?? new JArray()).OfType<JObject>())
        {
            if (ProposalJson.StringOf(item[keyField]) is string key && !byKey.ContainsKey(key))
            {
                byKey[key] = item;
            }
        }

        return byKey;
    }

    private static string SectionTitle(JObject section)
    {
        string? title = ProposalJson.StringOf(section[ProposalJson.TITLE]);
        return string.IsNullOrWhiteSpace(title) ? ProposalJson.StringOf(section[ProposalJson.NAME]) ?? string.Empty : title;
    }

    private static string? BlockTypeOf(JObject block)
    {
        JToken? type = block[ProposalJson.TYPE];
        if (type?.Type == JTokenType.Integer && Enum.IsDefined(typeof(BlockTypes), type.Value<int>()))
        {
            return ((BlockTypes)type.Value<int>()).ToString();
        }

        return ProposalJson.StringOf(type);
    }

    private static bool IsOutbound(string? type)
    {
        return type is not null && _outboundTypes.Contains(type);
    }

    private static bool IsLinked(JObject node)
    {
        return ProposalJson.StringOf(node[ProposalJson.SHARED_COMPONENT_ID]) is { Length: > 0 };
    }

    /// <summary>
    /// <c>Scope:Key:What</c>. A key the agent made so long the id would not fit a decision row is
    /// replaced by its digest; the id stays unique.
    /// </summary>
    private static string ChangeId(ProposedChangeScope scope, string? key, string what)
    {
        string id = string.Join(ID_SEPARATOR, scope.ToString(), key ?? string.Empty, what);
        if (id.Length <= MAX_CHANGE_ID_LENGTH)
        {
            return id;
        }

        string digest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key ?? string.Empty)));
        string shortWhat = what.Length > MAX_FIELD_IN_ID ? what[..MAX_FIELD_IN_ID] : what;
        return string.Join(ID_SEPARATOR, scope.ToString(), HASHED_KEY_PREFIX + digest, shortWhat);
    }

    private static string? BlockKeyFromPath(Tree? tree, string message)
    {
        if (tree is null)
        {
            return null;
        }

        Match match = BlockPath().Match(message);
        if (!match.Success)
        {
            return null;
        }

        int sectionIndex = int.Parse(match.Groups[SECTION_GROUP].Value, CultureInfo.InvariantCulture);
        int blockIndex = int.Parse(match.Groups[BLOCK_GROUP].Value, CultureInfo.InvariantCulture);

        return tree.Blocks.FirstOrDefault(block => block.SectionIndex == sectionIndex && block.BlockIndex == blockIndex)?.Key;
    }

    /// <summary>
    /// A block's position as the checks write it: <c>Sections[2].Blocks[1]</c> from the structural
    /// validator, <c>/Sections/2/Blocks/1</c> from the schema.
    /// </summary>
    [GeneratedRegex(@"Sections(?:\[|/)(?<section>\d+)\]?(?:\.|/)Blocks(?:\[|/)(?<block>\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex BlockPath();

    private static bool MentionsTag(string message, string tag)
    {
        return message.Contains($"'{tag}'", StringComparison.Ordinal)
            || message.Contains($"{{{tag}}}", StringComparison.Ordinal)
            || message.Contains($"\"{tag}\"", StringComparison.Ordinal);
    }

    /// <summary>
    /// The summary's entries as lookups, so each node is checked in constant time. A section named
    /// <c>new-</c> and its title, as an agent names a section it adds, also matches by that title.
    /// </summary>
    private sealed class SummaryIndex
    {
        private readonly HashSet<string> _blocks = new(StringComparer.Ordinal);
        private readonly HashSet<string> _sections = new(StringComparer.OrdinalIgnoreCase);

        public SummaryIndex(IEnumerable<ProposalSummaryEntry> summary)
        {
            foreach (ProposalSummaryEntry entry in summary)
            {
                if (entry.StableBlockId is string block)
                {
                    _ = _blocks.Add(block);
                }

                if (entry.Section is string section)
                {
                    _ = _sections.Add(section);
                    if (section.StartsWith(ProposalJson.NEW_KEY_PREFIX, StringComparison.OrdinalIgnoreCase))
                    {
                        _ = _sections.Add(section[ProposalJson.NEW_KEY_PREFIX.Length..]);
                    }
                }
            }
        }

        public bool HasBlock(string key, string? tag)
        {
            return _blocks.Contains(key) || (tag is not null && _blocks.Contains(tag));
        }

        public bool HasSection(string key, string title)
        {
            return _sections.Contains(key) || _sections.Contains(title);
        }
    }

    private sealed record BlockIdentity(string? Name, string? Tag, string? BlockType);

    private sealed record SectionNode(string Key, JObject Json);

    private sealed record BlockNode(string Key, string SectionKey, JObject Json, bool SectionLinked, int SectionIndex, int BlockIndex);

    /// <summary>One side's sections and blocks, keyed by stable id.</summary>
    private sealed class Tree
    {
        public List<SectionNode> Sections { get; } = [];

        public List<BlockNode> Blocks { get; } = [];

        public Dictionary<string, SectionNode> SectionsByKey { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, BlockNode> BlocksByKey { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// A node keeps its stable id when the id is used once on its side; a node without one is
        /// keyed by its row id, on both sides, so a workflow saved before stable ids existed still
        /// pairs. A repeated id makes the node new, with a generated key.
        /// </summary>
        public static Tree Of(JObject root, bool isBase)
        {
            var tree = new Tree();
            int newSections = 0;
            int newBlocks = 0;
            int sectionIndex = 0;

            foreach (JObject section in (root[ProposalJson.SECTIONS] as JArray ?? new JArray()).OfType<JObject>())
            {
                string sectionKey = KeyOf(section, ProposalJson.STABLE_SECTION_ID, tree.SectionsByKey)
                    ?? (isBase ? BASE_KEY_PREFIX : string.Empty) + NEW_SECTION_KEY_PREFIX + (++newSections).ToString(CultureInfo.InvariantCulture);
                var sectionNode = new SectionNode(sectionKey, section);
                tree.Sections.Add(sectionNode);
                tree.SectionsByKey[sectionKey] = sectionNode;
                bool sectionLinked = IsLinked(section);

                int blockIndex = 0;
                foreach (JObject block in (section[ProposalJson.BLOCKS] as JArray ?? new JArray()).OfType<JObject>())
                {
                    string blockKey = KeyOf(block, ProposalJson.STABLE_BLOCK_ID, tree.BlocksByKey)
                        ?? (isBase ? BASE_KEY_PREFIX : string.Empty) + NEW_BLOCK_KEY_PREFIX + (++newBlocks).ToString(CultureInfo.InvariantCulture);
                    var blockNode = new BlockNode(blockKey, sectionKey, block, sectionLinked, sectionIndex, blockIndex);
                    tree.Blocks.Add(blockNode);
                    tree.BlocksByKey[blockKey] = blockNode;
                    blockIndex++;
                }

                sectionIndex++;
            }

            return tree;
        }

        private static string? KeyOf<T>(JObject node, string stableIdField, Dictionary<string, T> claimed)
        {
            string? stableId = ProposalJson.StringOf(node[stableIdField]);
            if (string.IsNullOrWhiteSpace(stableId))
            {
                stableId = ProposalJson.StringOf(node[ProposalJson.ID]) is string id ? ID_KEY_PREFIX + id : null;
            }

            return stableId is null || claimed.ContainsKey(stableId) ? null : stableId;
        }
    }
}

using IniaAi.Common;
using IniaAi.Common.Reflection;
using IniaAi.Common.Serialization;
using IniaAi.Domain.Abstractions.Entities;
using IniaAi.Domain.Common;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Properties;
using IniaAi.Domain.Enums;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace IniaAi.Domain.Entities.Workflows;

public class Section : Entity<Section>, ITemplatable, ISharedComponentLinkable
{
    private const int WIDEST_TAG_COUNTER_LENGTH = 11;

    [Required]
    [StringLength(450)]
    [ForeignKey(nameof(Workflow))]
    [JsonIgnore]
    [CloneIgnore]
    public string WorkflowId { get; set; } = string.Empty;

    [Required]
    [JsonIgnore]
    [CloneIgnore]
    public Workflow Workflow { get; set; } = default!;

    [JsonProperty(nameof(Blocks))]
    [CloneIgnore]
    [InverseProperty(nameof(Block.Section))]
    public ICollection<Block> Blocks { get; private set; } = [];

    public bool ShouldSerializeBlocks()
    {
        return Blocks.Count > 0;
    }

    [StringLength(450)]
    [JsonProperty(nameof(Name))]
    public string Name { get; set; } = string.Empty;

    [StringLength(450)]
    [JsonProperty(nameof(Title))]
    public string Title { get; set; } = string.Empty;

    [JsonProperty(nameof(Description), NullValueHandling = NullValueHandling.Ignore)]
    public string? Description { get; set; }

    [JsonProperty(nameof(Order))]
    public int Order { get; set; }

    [JsonProperty(nameof(GroupColor), NullValueHandling = NullValueHandling.Ignore)]
    public GroupColor? GroupColor { get; set; }

    [JsonProperty(nameof(TextInputTemplate), NullValueHandling = NullValueHandling.Ignore)]
    public string? TextInputTemplate { get; set; }

    [JsonProperty(nameof(ParameterizedConfig), NullValueHandling = NullValueHandling.Ignore)]
    public string? ParameterizedConfig { get; set; }

    [JsonProperty(nameof(ExpandedFromParameterizedKey), NullValueHandling = NullValueHandling.Ignore)]
    public string? ExpandedFromParameterizedKey { get; set; }

    [JsonProperty(nameof(Properties), NullValueHandling = NullValueHandling.Ignore)]
    public SectionProperties Properties { get; set; } = new();

    public bool ShouldSerializeProperties()
    {
        return !Properties.IsDefault();
    }

    [JsonProperty(nameof(StableSectionId), NullValueHandling = NullValueHandling.Ignore)]
    [StringLength(450)]
    public string? StableSectionId { get; set; }

    /// <summary>
    /// Link record marking this section as a materialized copy of a Library shared component; null
    /// on plain local sections. Same lifecycle contract as the Block quartet: copied verbatim by
    /// every clone path, never re-minted or suffixed, hash-excluded, payload-kept, and never usable
    /// as a unique key within a workflow.
    /// </summary>
    [JsonProperty(nameof(SharedComponentId), NullValueHandling = NullValueHandling.Ignore)]
    [StringLength(450)]
    [PlaceholderReplacementIgnore]
    public string? SharedComponentId { get; set; }

    [JsonProperty(nameof(SharedComponentLinkState), NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(TolerantStringEnumConverter))]
    public SharedComponentLinkState? SharedComponentLinkState { get; set; }

    [JsonProperty(nameof(SharedComponentVersionHash), NullValueHandling = NullValueHandling.Ignore)]
    [StringLength(64)]
    [PlaceholderReplacementIgnore]
    public string? SharedComponentVersionHash { get; set; }

    [JsonProperty(nameof(SharedComponentBindings), NullValueHandling = NullValueHandling.Ignore)]
    [PlaceholderReplacementIgnore]
    public Dictionary<string, string>? SharedComponentBindings { get; set; }

    public bool ShouldSerializeSharedComponentBindings()
    {
        return SharedComponentBindings is { Count: > 0 };
    }

    [JsonProperty(
        nameof(SharedComponentSlotStates),
        NullValueHandling = NullValueHandling.Ignore,
        ItemConverterType = typeof(TolerantStringEnumConverter))]
    [PlaceholderReplacementIgnore]
    public Dictionary<string, SharedComponentSlotState>? SharedComponentSlotStates { get; set; }

    public bool ShouldSerializeSharedComponentSlotStates()
    {
        return SharedComponentSlotStates is { Count: > 0 };
    }

    [NotMapped]
    [JsonProperty(nameof(Entries), NullValueHandling = NullValueHandling.Ignore)]
    [PlaceholderReplacementIgnore]
    public List<SectionEntry>? Entries { get; set; }

    public bool ShouldSerializeEntries()
    {
        return Entries is { Count: > 0 };
    }

    [NotMapped]
    [JsonIgnore]
    public bool IsSeparator => Properties.IsSeparator;

    [NotMapped]
    [JsonProperty(nameof(ExecutionState), NullValueHandling = NullValueHandling.Ignore)]
    public WorkflowExecutionState? ExecutionState { get; set; }

    [CloneIgnore]
    [JsonProperty(nameof(Configuration), NullValueHandling = NullValueHandling.Ignore)]
    public SectionConfiguration? Configuration { get; set; }

    public Section()
    {
    }

    public Section(string name, Workflow parent, int index = -1)
    {
        Name = name;
        Workflow = parent;
        WorkflowId = parent.Id;

        if (index == -1)
        {
            parent.Add(this);
        }
        else
        {
            parent.Insert(index, this);
        }

        ReMintStableSectionId();
    }

    internal void Add(Block block)
    {
        if (IsSeparator)
        {
            throw new InvalidOperationException("Cannot add a block to a separator section.");
        }

        block.Order = Blocks.Count;
        block.Section = this;
        block.SectionId = Id;
        Blocks.Add(block);
    }

    internal void Insert(int index, Block block)
    {
        if (IsSeparator)
        {
            throw new InvalidOperationException("Cannot insert a block into a separator section.");
        }

        var ordered = Blocks.OrderBy(b => b.Order).ToList();
        index = Math.Clamp(index, 0, ordered.Count);

        ordered.Insert(index, block);
        block.Section = this;
        block.SectionId = Id;

        for (int i = 0; i < ordered.Count; i++)
        {
            ordered[i].Order = i;
        }

        Blocks.Clear();
        foreach (Block b in ordered)
        {
            Blocks.Add(b);
        }
    }

    public Block this[int index] => Blocks.FirstOrDefault(b => b.Order == index)
        ?? throw new IndexOutOfRangeException($"Block at index {index} not found.");

    public void Remove(Block block)
    {
        if (!Blocks.Remove(block))
        {
            return;
        }

        NormalizeBlockOrdering();
    }

    public Block RemoveBlockAt(int index)
    {
        Block block = Blocks.FirstOrDefault(b => b.Order == index)
            ?? throw new IndexOutOfRangeException($"Block at index {index} not found.");

        Remove(block);

        return block;
    }

    public bool ReplacementTagIsInUse(string replacementTag)
    {
        return Blocks.Any(b => b.ReplacementTag == replacementTag);
    }

    public Block? FindBlockByReplacementTag(string replacementTag)
    {
        return Blocks.FirstOrDefault(b => b.ReplacementTag == replacementTag);
    }

    public List<Block> FindBlocksByReplacementTag(string replacementTag)
    {
        return [.. Blocks.Where(b => b.ReplacementTag == replacementTag)];
    }

    public string MintFreeReplacementTag(string prefix, int firstIndex)
    {
        HashSet<string> taken = ReplacementTagsInUse();
        int prefixRoom = Block.MAX_REPLACEMENT_TAG_LENGTH - WIDEST_TAG_COUNTER_LENGTH;
        string boundedPrefix = prefix.Length <= prefixRoom ? prefix : prefix[..prefixRoom];

        int candidate = firstIndex;
        string tag;
        do
        {
            tag = $"{boundedPrefix}{candidate}";
            candidate++;
        }
        while (taken.Contains(tag));

        return tag;
    }

    public string FreeReplacementTagFrom(string preferredTag, Block? excluding = null)
    {
        HashSet<string> taken = ReplacementTagsInUse(excluding);

        string preferred = Block.ComposeTagWithinLimit(preferredTag, string.Empty);
        if (!taken.Contains(preferred))
        {
            return preferred;
        }

        int suffix = 2;
        string tag;
        do
        {
            tag = Block.ComposeTagWithinLimit(preferredTag, $"_{suffix}");
            suffix++;
        }
        while (taken.Contains(tag));

        return tag;
    }

    private HashSet<string> ReplacementTagsInUse(Block? excluding = null)
    {
        return new HashSet<string>(
            Blocks
                .Where(block => !ReferenceEquals(block, excluding) && !string.IsNullOrEmpty(block.ReplacementTag))
                .Select(block => block.ReplacementTag),
            StringComparer.FromComparison(IniaAi.Common.Constants.ReplacementTags.Comparison));
    }

    public void ExposeAllBlocksAsOutputs()
    {
        foreach (Block block in Blocks)
        {
            block.IsOutputSource = true;
            block.OutputTitleTemplate = !string.IsNullOrWhiteSpace(block.OutputTitleTemplate)
                ? block.OutputTitleTemplate
                : !string.IsNullOrWhiteSpace(block.Name)
                    ? block.Name
                    : block.ReplacementTag;
        }
    }

    [NotMapped]
    [JsonIgnore]
    public bool HasOutputBlocks => Blocks.Any(b => b.IsOutputSource);

    public bool RequiredDocumentsNotProvided()
    {
        return Blocks.Any(b => b.ShouldUseDocuments())
            && Blocks.Where(b => b.ShouldUseDocuments()).All(b => b.RequiredDocumentsNotProvided());
    }

    public void Clear()
    {
        Blocks.Clear();
    }

    /// <summary>
    /// Re-mints the section's stable id and every block's. For authoring copies (duplicate section,
    /// copy to another workflow) — execution clones must NOT call this, since baseline/rerun
    /// alignment depends on the clone keeping the template's ids.
    /// </summary>
    public void ReMintStableIds()
    {
        ReMintStableSectionId();

        foreach (Block block in Blocks)
        {
            block.ReMintStableBlockId();
        }
    }

    [MemberNotNull(nameof(StableSectionId))]
    public void ReMintStableSectionId()
    {
        StableSectionId = Guid.NewGuid().ToString("N");
    }

    public void SuffixStableSectionId(string suffix)
    {
        if (!string.IsNullOrEmpty(StableSectionId))
        {
            StableSectionId = $"{StableSectionId}_{suffix}";
        }
    }

    public void CloneTo(Section destination)
    {
        destination.Clear();

        CloneHelper.CopyScalars(this, destination);
        destination.Properties = Properties.Clone();
        destination.Configuration = Configuration?.Clone();

        foreach (Block block in Blocks.OrderBy(b => b.Order))
        {
            _ = block.CloneToSection(destination);
        }
    }

    /// <summary>
    /// Grafts a shared component definition's section-level content onto this consumer copy in
    /// place: shared scalars are copied from <paramref name="definition"/> (Properties and
    /// Configuration as deep clones) while Order, the presentation (Name, Title, GroupColor),
    /// StableSectionId, the link quartet, parent wiring and execution state stay untouched. Inner
    /// blocks are never touched — slot-level reconciliation owns them.
    /// </summary>
    public void ApplySharedDefinition(Section definition)
    {
        int order = Order;
        string name = Name;
        string title = Title;
        GroupColor? groupColor = GroupColor;
        string? stableSectionId = StableSectionId;
        string? sharedComponentId = SharedComponentId;
        SharedComponentLinkState? sharedComponentLinkState = SharedComponentLinkState;
        string? sharedComponentVersionHash = SharedComponentVersionHash;
        Dictionary<string, string>? sharedComponentBindings = SharedComponentBindings;
        Dictionary<string, SharedComponentSlotState>? sharedComponentSlotStates = SharedComponentSlotStates;
        List<SectionEntry>? entries = Entries;
        WorkflowExecutionState? executionState = ExecutionState;

        CloneHelper.CopyScalars(definition, this);
        Properties = definition.Properties.Clone();
        Configuration = definition.Configuration?.Clone();

        Order = order;
        Name = name;
        Title = title;
        GroupColor = groupColor;
        StableSectionId = stableSectionId;
        SharedComponentId = sharedComponentId;
        SharedComponentLinkState = sharedComponentLinkState;
        SharedComponentVersionHash = sharedComponentVersionHash;
        SharedComponentBindings = sharedComponentBindings;
        SharedComponentSlotStates = sharedComponentSlotStates;
        Entries = entries;
        ExecutionState = executionState;
    }

    public List<SectionEntry> EffectiveEntries()
    {
        if (Entries is { Count: > 0 })
        {
            return [.. Entries];
        }

        return [.. Blocks
            .OrderBy(b => b.Order)
            .Select(b => new SectionEntry
            {
                SlotTag = b.ReplacementTag,
                Kind = SectionEntryKind.Inline,
            })];
    }

    public void MoveToWorkflow(Workflow destination, int index = -1)
    {
        index = index == -1 ? destination.Sections.Count : index;

        if (destination.Id == WorkflowId)
        {
            Workflow.MoveSection(Order, index);

            return;
        }

        Workflow oldParent = Workflow;
        oldParent.Remove(this);

        Workflow = destination;
        WorkflowId = destination.Id;
        destination.Add(this);
        destination.MoveSection(Order, index);
    }

    public Section CloneToWorkflow(Workflow destinationParent, int targetIndex, string? nameOverride = null)
    {
        string sectionName = nameOverride ?? Name;

        var copied = new Section(sectionName, destinationParent, targetIndex);
        CloneTo(copied);
        copied.ReMintStableIds();

        copied.Order = targetIndex;
        copied.Name = sectionName;
        copied.Workflow = destinationParent;
        copied.WorkflowId = destinationParent.Id;

        destinationParent.NormalizeSectionOrdering();

        return copied;
    }

    public void NormalizeBlockOrdering()
    {
        var ordered = Blocks.OrderBy(b => b.Order).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            ordered[i].Order = i;
        }

        Blocks.Clear();
        foreach (Block b in ordered)
        {
            Blocks.Add(b);
        }
    }

    internal void MoveBlock(int fromIndex, int toIndex)
    {
        var ordered = Blocks.OrderBy(b => b.Order).ToList();
        if (fromIndex < 0 || fromIndex >= ordered.Count)
        {
            throw new IndexOutOfRangeException("From index out of range.");
        }

        Block block = ordered[fromIndex];
        ordered.RemoveAt(fromIndex);

        if (toIndex < 0)
        {
            toIndex = 0;
        }
        else if (toIndex > ordered.Count)
        {
            toIndex = ordered.Count;
        }

        ordered.Insert(toIndex, block);

        for (int i = 0; i < ordered.Count; i++)
        {
            ordered[i].Order = i;
        }

        Blocks.Clear();
        foreach (Block b in ordered)
        {
            Blocks.Add(b);
        }
    }

    public void GetLoopStartAndEnd(out Block? loopStart, out Block? loopEnd)
    {
        FindLoopStartAndEnd(out loopStart, out loopEnd);

        if (loopStart is not null && loopEnd is null)
        {
            throw new InvalidOperationException("Loop start found without end.");
        }

        if (loopEnd is not null && loopStart is null)
        {
            throw new InvalidOperationException("Loop end found without start.");
        }
    }

    private void FindLoopStartAndEnd(out Block? loopStart, out Block? loopEnd)
    {
        loopStart = null;
        loopEnd = null;
        foreach (Block block in Blocks)
        {
            if (block.Type is BlockTypes.LoopStart)
            {
                loopStart = block;
                continue;
            }

            if (block.Type is BlockTypes.LoopEnd)
            {
                loopEnd = block;
                break;
            }
        }
    }

    public IReadOnlySet<string> GetLoopBodyBlockIds()
    {
        if (!TryGetLoopBodyBounds(out int loopStartOrder, out int loopEndOrder))
        {
            return new HashSet<string>();
        }

        return Blocks
            .Where(b => b.Order >= loopStartOrder && b.Order <= loopEndOrder)
            .Select(b => b.Id)
            .ToHashSet();
    }

    private bool TryGetLoopBodyBounds(out int loopStartOrder, out int loopEndOrder)
    {
        loopStartOrder = 0;
        loopEndOrder = 0;

        FindLoopStartAndEnd(out Block? loopStart, out Block? loopEnd);

        if (loopStart is null || loopEnd is null || loopEnd.Order < loopStart.Order)
        {
            return false;
        }

        loopStartOrder = loopStart.Order;
        loopEndOrder = loopEnd.Order;
        return true;
    }

    public void ProcessLoops(Dictionary<string, string> sharedInputs)
    {
        NormalizeBlockOrdering();

        GetLoopStartAndEnd(out Block? loopStart, out Block? loopEnd);

        if (loopStart is null || loopEnd is null)
        {
            return;
        }

        List<string> listToLoopOn = CreateLoopList(sharedInputs, loopStart);

        if (listToLoopOn.Count == 0)
        {
            return;
        }

        string? listTitle = (loopStart.Properties as LoopStartProperties)?.ListTitle;

        if (listTitle is null)
        {
            return;
        }

        var blocksToLoop = Blocks
            .Where(q => q.Order > loopStart.Order && q.Order < loopEnd.Order)
            .ToList();

        var blocksAfterLoop = Blocks
            .Where(q => q.Order > loopEnd.Order)
            .ToList();

        _ = RemoveBlockAt(loopStart.Order);
        _ = RemoveBlockAt(loopEnd.Order);

        Dictionary<int, List<Block>> newBlocks = CloneBlocksInLoop(
            listToLoopOn,
            listTitle,
            blocksToLoop,
            loopStart);

        UpdateReplacementTags(
            listToLoopOn.Count,
            listTitle,
            blocksToLoop,
            newBlocks);

        UpdateLoopedBlocksReplacementTagsInBlocksAfterLoop(
            listToLoopOn.Count,
            listTitle,
            blocksToLoop,
            blocksAfterLoop);

        NormalizeBlockOrdering();

        UpdateLoopedBlocksReplacementTagsInSectionsAfterCurrent(
            listToLoopOn.Count,
            listTitle,
            blocksToLoop);
    }

    private List<string> CreateLoopList(
        Dictionary<string, string> sharedInputs,
        Block loopStart)
    {
        string? loopedListTemplate = (loopStart.Properties as LoopStartProperties)?.LoopedListTemplate;

        if (string.IsNullOrEmpty(loopedListTemplate))
        {
            return [];
        }

        List<string> placeholders = loopStart.GetAllPlaceholders();
        var replacementDict = new Dictionary<string, string?>();

        foreach (string placeholder in placeholders)
        {
            string? value = Workflow?.GetBlockOutput(placeholder);

            if (value is null)
            {
                continue;
            }

            replacementDict[placeholder] = value;
        }

        foreach ((string key, string value) in sharedInputs)
        {
            if (value is null)
            {
                continue;
            }

            replacementDict[key] = value;
        }

        string listTemplate = loopedListTemplate;

        foreach ((string key, string? value) in replacementDict)
        {
            if (value is null)
            {
                continue;
            }

            listTemplate = listTemplate.Replace($"{{{key}}}", value);
        }

        return [.. listTemplate
            .Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries)];
    }

    private Dictionary<int, List<Block>> CloneBlocksInLoop(
        List<string> listToLoopOn,
        string listTitle,
        List<Block> blocksToLoop,
        Block loopStart)
    {
        Dictionary<int, List<Block>> newBlocks = [];
        int currentOrder = 0;

        // Loop-item identity anchors on the LoopStart block's authored stable id, which survives
        // section-title and list-title renames and is unique per loop. A LoopStart without one
        // (template not saved since stable ids were introduced) yields colliding ids across loops,
        // which the matcher reports as Ambiguous — loud, never a wrong match.
        string loopIdentity = loopStart.StableBlockId ?? string.Empty;

        foreach (Block blockToClone in blocksToLoop)
        {
            _ = RemoveBlockAt(blockToClone.Order);
        }

        for (int i = 0; i < listToLoopOn.Count; i++)
        {
            string listItem = listToLoopOn[i];
            List<Block> iteration = [];
            newBlocks.Add(i, iteration);

            var loopItemBlock = new Block(BlockTypes.Merge, this, currentOrder++, $"{listTitle}_{i}")
            {
                Name = listItem,
                StableBlockId = $"{Block.LOOP_ITEM_STABLE_ID_PREFIX}{loopIdentity}_{i}",
                Properties = new MergeProperties { TextTemplate = listItem },
            };
            iteration.Add(loopItemBlock);

            foreach (Block blockToClone in blocksToLoop)
            {
                Block clone = blockToClone.CloneToSection(
                    this,
                    currentOrder++,
                    $"{listTitle}_{blockToClone.ReplacementTag}_{i}");
                clone.Name = $"{listItem} - {blockToClone.Name}";
                clone.SuffixStableBlockId($"{listTitle}_{i}");
                clone.SuffixDataOutputName(i.ToString(CultureInfo.InvariantCulture));

                foreach (Document document in blockToClone.Documents)
                {
                    clone.Documents.Add(document);
                }

                ReplaceListTitleWithListItemKey(listTitle, i, clone);

                iteration.Add(clone);
            }
        }

        return newBlocks;
    }

    private static void ReplaceListTitleWithListItemKey(
        string listTitle,
        int listItemIndex,
        Block clone)
    {
        Dictionary<string, string?> replacements = new();
        ReplacementTagRenames.AddTagRename(replacements, listTitle, $"{listTitle}_{listItemIndex}");

        clone.ReplaceAllForExpansion(replacements);
    }

    private void UpdateLoopedBlocksReplacementTagsInSectionsAfterCurrent(
        int listLength,
        string listTitle,
        List<Block> blocksToLoop)
    {
        if (Workflow is null)
        {
            return;
        }

        var laterSiblings = Workflow.Sections
            .Where(s => s.Order > Order)
            .ToList();

        foreach (Section sibling in laterSiblings)
        {
            foreach (Block block in sibling.Blocks)
            {
                foreach (Block originalBlock in blocksToLoop)
                {
                    AdjustBlockTagsAfterLoop(
                        listLength,
                        listTitle,
                        originalBlock.ReplacementTag,
                        block);
                }
            }
        }
    }

    private static void UpdateLoopedBlocksReplacementTagsInBlocksAfterLoop(
        int listLength,
        string listTitle,
        List<Block> blocksToLoop,
        List<Block> blocksAfterLoop)
    {
        foreach (Block block in blocksAfterLoop)
        {
            foreach (Block originalBlock in blocksToLoop)
            {
                AdjustBlockTagsAfterLoop(
                    listLength,
                    listTitle,
                    originalBlock.ReplacementTag,
                    block);
            }
        }
    }

    private static void AdjustBlockTagsAfterLoop(
        int listLength,
        string listTitle,
        string replacementTag,
        Block block)
    {
        // "\n", not AppendLine: Environment.NewLine would make the prompt differ on Windows.
        string replacementForTag = string.Join(
            "\n",
            Enumerable.Range(0, listLength).Select(i => $"{{{listTitle}_{replacementTag}_{i}}}"));

        Dictionary<string, string?> replacements = new()
        {
            [$"{{{replacementTag}}}"] = replacementForTag,
        };

        block.ReplaceAllForExpansion(replacements);
    }

    private static void UpdateReplacementTags(
        int listLength,
        string listTitle,
        List<Block> blocksToLoop,
        Dictionary<int, List<Block>> newBlocks)
    {
        for (int i = 0; i < listLength; i++)
        {
            List<Block> iteration = newBlocks[i];

            foreach (Block block in blocksToLoop)
            {
                foreach (Block iterationBlock in iteration)
                {
                    Dictionary<string, string?> replacements = new();
                    ReplacementTagRenames.AddTagRename(replacements, block.ReplacementTag, $"{listTitle}_{block.ReplacementTag}_{i}");

                    iterationBlock.ReplaceAllForExpansion(replacements);
                }
            }
        }
    }

    public void ReplaceAll(
        Dictionary<string, string?> replacements,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        PlaceholderReflectionHelper.ReplaceAll(this, false, replacements, stringComparison);

        foreach (Block block in Blocks)
        {
            block.ReplaceAll(replacements, stringComparison);
        }
    }

    /// <summary>
    /// Renames block tags and rewrites every brace-delimited reference to them. Renames are applied
    /// simultaneously: a rename's target may equal another rename's source without cascading.
    /// </summary>
    public void ApplyReplacementTagRenames(IReadOnlyDictionary<string, string> renames)
    {
        if (renames.Count == 0)
        {
            return;
        }

        foreach (Block block in Blocks)
        {
            if (renames.TryGetValue(block.ReplacementTag, out string? renamedTag))
            {
                block.ReplacementTag = renamedTag;
            }
        }

        RenameTagReferences(renames);
    }

    /// <summary>
    /// Rewrites brace-delimited tag references in this section's own fields and all current blocks
    /// without renaming any block's own tag, with the same simultaneous-application guarantee as
    /// <see cref="ApplyReplacementTagRenames"/>.
    /// </summary>
    public void RenameTagReferences(IReadOnlyDictionary<string, string> renames)
    {
        if (renames.Count == 0)
        {
            return;
        }

        (Dictionary<string, string?> toTemp, Dictionary<string, string?> toFinal) =
            ReplacementTagRenames.BuildSimultaneousMaps(renames);
        ReplaceAll(toTemp);
        ReplaceAll(toFinal);
    }

    public bool HasPlaceholder(
        string placeholder,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        if (PlaceholderReflectionHelper.HasPlaceholder(this, placeholder, stringComparison))
        {
            return true;
        }

        return Blocks.Any(b => b.HasPlaceholder(placeholder, stringComparison));
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

    public void ClearExecutionState()
    {
        ExecutionState = null;

        foreach (Block block in Blocks)
        {
            block.ClearExecutionState();
        }
    }

    public void UpdateExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState ??= new WorkflowExecutionState(this);
        PlaceholderReflectionHelper.ReplaceAll(ExecutionState, asPlaceHolder, inputs, stringComparison);
    }

    public void SetExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState = new WorkflowExecutionState(this);
        PlaceholderReflectionHelper.ReplaceAll(ExecutionState, asPlaceHolder, inputs, stringComparison);
    }
}

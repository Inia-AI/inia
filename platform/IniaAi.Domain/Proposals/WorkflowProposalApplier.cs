using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.Domain.Proposals;

/// <summary>
/// Applies chosen changes of a proposal to a live workflow in memory, and takes them back. Each
/// field is checked on its own: when the live value is no longer the base value the agent saw,
/// the change is reported as a conflict with both values instead of being applied, unless the
/// caller chose the proposed value for it. A change whose live value already equals the proposed
/// value counts as applied. Execution order is not a conflict: applying renumbers a section's
/// blocks, so an <c>Order</c> edit always applies and a removal ignores <c>Order</c>. An undo
/// leaves alone anything the user changed after the apply and reports it as a conflict. Nothing
/// here saves.
/// </summary>
public static class WorkflowProposalApplier
{
    public const string NOT_FOUND_REASON = "It is no longer in the workflow.";
    public const string NO_SECTION_REASON = "The section it goes into is not in the workflow. Accept that section first.";
    public const string SECTION_NOT_EMPTY_REASON =
        "The section still holds blocks the agent moved elsewhere, or blocks added since. Accept those moves first.";
    public const string ITEM_EXISTS_REASON = "The workflow already has one with this name.";

    private const string ID_KEY_PREFIX = "id:";

    private static readonly HashSet<string> _orderKeys = new(StringComparer.Ordinal) { ProposalJson.ORDER };

    /// <param name="takeProposed">Changes whose conflict the user settled for the proposed value.</param>
    /// <param name="mintedKeys">The stable ids earlier accepts gave the proposal's added nodes, by
    /// the key the proposal uses for them, so a later change can find a section accepted before.</param>
    public static ProposalApplyResult Apply(
        Workflow live,
        IReadOnlyList<ProposedChange> changes,
        IReadOnlySet<string> takeProposed,
        IReadOnlyDictionary<string, string>? mintedKeys = null)
    {
        var context = new ApplyContext(live, takeProposed, mintedKeys);
        context.Capture(changes);

        foreach (ProposedChange change in changes.OrderBy(Stage))
        {
            context.Apply(change);
        }

        context.Normalize();

        return new ProposalApplyResult(context.Applied, context.Conflicts, context.NotApplied, context.Undo);
    }

    /// <summary>
    /// Takes back every step of <paramref name="undo"/>, newest first, then the order of the
    /// sections the apply touched. A step whose node the user changed since is skipped and its
    /// change reported as a conflict carrying the value the apply wrote and the live value.
    /// </summary>
    public static ProposalUndoOutcome Undo(Workflow live, ProposalUndo undo)
    {
        return Undo(live, undo, only: null);
    }

    /// <summary>
    /// Takes back only the steps of <paramref name="only"/>'s changes, newest first, then the order
    /// of the blocks in the sections those steps touched, and of the sections when a step added,
    /// removed or reordered one. The steps left stay in <paramref name="undo"/> for a later undo.
    /// </summary>
    public static ProposalUndoOutcome Undo(Workflow live, ProposalUndo undo, IReadOnlySet<string>? only)
    {
        List<ProposalUndoStep> chosen = only is null ? [.. undo.Steps] : [.. undo.Steps.Where(step => only.Contains(step.ChangeId))];
        var conflicts = new List<ProposalConflict>();
        for (int i = chosen.Count - 1; i >= 0; i--)
        {
            if (UndoStep(live, chosen[i]) is ProposalConflict conflict)
            {
                conflicts.Add(conflict);
            }
        }

        HashSet<string>? sections = only is null ? null : SectionsOf(live, chosen);
        bool sectionOrder = only is null || chosen.Any(static step => step.Scope == ProposedChangeScope.Section);
        foreach (Section section in live.Sections)
        {
            if (sectionOrder && undo.SectionOrders.TryGetValue(SectionKey(section), out int order))
            {
                section.Order = order;
            }

            if (sections is not null && !sections.Contains(SectionKey(section)))
            {
                continue;
            }

            bool touched = false;
            foreach (Block block in section.Blocks)
            {
                if (undo.BlockOrders.TryGetValue(BlockKey(block), out int blockOrder))
                {
                    block.Order = blockOrder;
                    touched = true;
                }
            }

            if (touched)
            {
                section.NormalizeBlockOrdering();
            }
        }

        if (sectionOrder && undo.SectionOrders.Count > 0)
        {
            live.NormalizeSectionOrdering();
        }

        if (only is not null)
        {
            _ = undo.Steps.RemoveAll(step => only.Contains(step.ChangeId));
        }

        return new ProposalUndoOutcome(conflicts);
    }

    /// <summary>The sections the steps' blocks sit in after the undo, and the sections they moved between.</summary>
    private static HashSet<string> SectionsOf(Workflow live, IEnumerable<ProposalUndoStep> steps)
    {
        var sections = new HashSet<string>(StringComparer.Ordinal);
        foreach (ProposalUndoStep step in steps)
        {
            if (step.Scope == ProposedChangeScope.Block && FindBlock(live, step.Key)?.Section is Section section)
            {
                _ = sections.Add(SectionKey(section));
            }

            foreach (string? key in new[] { step.SectionKey, step.IntoSectionKey })
            {
                if (key is not null)
                {
                    _ = sections.Add(key);
                }
            }
        }

        return sections;
    }

    private static int Stage(ProposedChange change)
    {
        return (change.Scope, change.Kind) switch
        {
            (ProposedChangeScope.Section, ProposedChangeKind.Added) => 0,
            (ProposedChangeScope.Block, ProposedChangeKind.Moved) => 1,
            (_, ProposedChangeKind.Added) => 2,
            (_, ProposedChangeKind.FieldEdited) => 3,
            (ProposedChangeScope.Section, ProposedChangeKind.Removed) => 5,
            _ => 4,
        };
    }

    private static ProposalConflict? UndoStep(Workflow live, ProposalUndoStep step)
    {
        switch (step.Action)
        {
            case ProposalUndoAction.SetField:
                if (Locate(live, step.Scope, step.Key) is not object target)
                {
                    return null;
                }

                JToken? current = ReadField(target, step.Scope, step.Field!);
                JToken? applied = Parse(step.AppliedJson);
                if (!ProposalJson.Same(current, applied) && step.Field != ProposalJson.ORDER)
                {
                    return new ProposalConflict(step.ChangeId, step.Field, step.Json, ProposalJson.Text(current), step.AppliedJson);
                }

                WriteField(live, target, step.Scope, step.Field!, Parse(step.Json), stripRowIds: false);
                return null;
            case ProposalUndoAction.RemoveAdded:
                if (Locate(live, step.Scope, step.Key) is not object added)
                {
                    return null;
                }

                JToken now = Comparable(ProposalJson.ToJson(added));
                if (step.AppliedJson is not null && !ProposalJson.Same(now, ProposalJson.Parse(step.AppliedJson)))
                {
                    return new ProposalConflict(step.ChangeId, null, null, ProposalJson.Text(now), step.AppliedJson);
                }

                Remove(live, step.Scope, step.Key);
                return null;
            case ProposalUndoAction.Restore:
                Restore(live, step);
                return null;
            case ProposalUndoAction.MoveBack:
                if (FindBlock(live, step.Key) is not Block moved || FindSection(live, step.SectionKey) is not Section from)
                {
                    return null;
                }

                string? currentSection = moved.Section is null ? null : SectionKey(moved.Section);
                if (step.IntoSectionKey is not null && currentSection != step.IntoSectionKey)
                {
                    return new ProposalConflict(
                        step.ChangeId, null, JsonConvert.SerializeObject(step.SectionKey), JsonConvert.SerializeObject(currentSection), JsonConvert.SerializeObject(step.IntoSectionKey));
                }

                MoveBlock(moved, from);
                return null;
            default:
                throw new InvalidOperationException($"Unknown undo action {step.Action}");
        }
    }

    private static void Restore(Workflow live, ProposalUndoStep step)
    {
        JToken json = ProposalJson.Parse(step.Json!);
        switch (step.Scope)
        {
            case ProposedChangeScope.Block:
                if (FindSection(live, step.SectionKey) is Section section && FindBlock(live, step.Key) is null)
                {
                    AttachBlock(section, ProposalJson.Deserialize<Block>(json));
                }

                break;
            case ProposedChangeScope.Section:
                if (FindSection(live, step.Key) is null)
                {
                    AttachSection(live, ProposalJson.Deserialize<Section>(json));
                }

                break;
            default:
                if (FindItem(live, step.Scope, step.Key) is null)
                {
                    AddItem(live, step.Scope, json);
                }

                break;
        }
    }

    private static JToken? Parse(string? json)
    {
        return json is null ? null : ProposalJson.Parse(json);
    }

    /// <summary>A node's content as a change compares it: no run evidence and no execution order.</summary>
    private static JToken Comparable(JToken node)
    {
        JToken withoutEvidence = ProposalJson.Without(node, ProposalJson.EvidenceKeys);
        return ProposalJson.Without(withoutEvidence, _orderKeys);
    }

    private static object? Locate(Workflow live, ProposedChangeScope scope, string? key)
    {
        return scope switch
        {
            ProposedChangeScope.Block => FindBlock(live, key),
            ProposedChangeScope.Section => FindSection(live, key),
            ProposedChangeScope.Workflow => live,
            _ => FindItem(live, scope, key),
        };
    }

    internal static string BlockKey(Block block)
    {
        return string.IsNullOrWhiteSpace(block.StableBlockId) ? ID_KEY_PREFIX + block.Id : block.StableBlockId;
    }

    internal static string SectionKey(Section section)
    {
        return string.IsNullOrWhiteSpace(section.StableSectionId) ? ID_KEY_PREFIX + section.Id : section.StableSectionId;
    }

    internal static Block? FindBlock(Workflow live, string? key)
    {
        return key is null
            ? null
            : live.Sections.SelectMany(static section => section.Blocks)
                .FirstOrDefault(block => block.StableBlockId == key || ID_KEY_PREFIX + block.Id == key);
    }

    internal static Section? FindSection(Workflow live, string? key)
    {
        return key is null
            ? null
            : live.Sections.FirstOrDefault(section => section.StableSectionId == key || ID_KEY_PREFIX + section.Id == key);
    }

    private static object? FindItem(Workflow live, ProposedChangeScope scope, string? key)
    {
        return scope switch
        {
            ProposedChangeScope.Variable => live.Variables.FirstOrDefault(item => item.ReplacementTag == key),
            ProposedChangeScope.DocumentClass => live.DocumentClasses.FirstOrDefault(item => item.Key == key),
            ProposedChangeScope.SignatureField => live.SignatureFields.FirstOrDefault(item => item.Name == key),
            ProposedChangeScope.RegressionField => live.RegressionFields.FirstOrDefault(item => item.Name == key),
            _ => null,
        };
    }

    private static void AddItem(Workflow live, ProposedChangeScope scope, JToken json)
    {
        switch (scope)
        {
            case ProposedChangeScope.Variable:
                WorkflowVariable variable = ProposalJson.Deserialize<WorkflowVariable>(json);
                variable.Workflow = live;
                variable.WorkflowId = live.Id;
                live.Variables.Add(variable);
                break;
            case ProposedChangeScope.DocumentClass:
                DocumentClass documentClass = ProposalJson.Deserialize<DocumentClass>(json);
                documentClass.Workflow = live;
                documentClass.WorkflowId = live.Id;
                live.DocumentClasses.Add(documentClass);
                break;
            case ProposedChangeScope.SignatureField:
                WorkflowSignatureField signatureField = ProposalJson.Deserialize<WorkflowSignatureField>(json);
                signatureField.Workflow = live;
                signatureField.WorkflowId = live.Id;
                live.SignatureFields.Add(signatureField);
                break;
            case ProposedChangeScope.RegressionField:
                WorkflowRegressionField regressionField = ProposalJson.Deserialize<WorkflowRegressionField>(json);
                regressionField.Workflow = live;
                regressionField.WorkflowId = live.Id;
                live.RegressionFields.Add(regressionField);
                break;
            default:
                throw new InvalidOperationException($"{scope} is not a workflow list");
        }
    }

    private static void Remove(Workflow live, ProposedChangeScope scope, string? key)
    {
        switch (scope)
        {
            case ProposedChangeScope.Block:
                if (FindBlock(live, key) is Block block)
                {
                    _ = block.Section!.Blocks.Remove(block);
                }

                break;
            case ProposedChangeScope.Section:
                if (FindSection(live, key) is Section section)
                {
                    _ = live.Sections.Remove(section);
                }

                break;
            case ProposedChangeScope.Variable:
                RemoveItem(live.Variables, FindItem(live, scope, key));
                break;
            case ProposedChangeScope.DocumentClass:
                RemoveItem(live.DocumentClasses, FindItem(live, scope, key));
                break;
            case ProposedChangeScope.SignatureField:
                RemoveItem(live.SignatureFields, FindItem(live, scope, key));
                break;
            case ProposedChangeScope.RegressionField:
                RemoveItem(live.RegressionFields, FindItem(live, scope, key));
                break;
            default:
                throw new InvalidOperationException($"{scope} cannot be removed");
        }
    }

    private static void RemoveItem<T>(ICollection<T> items, object? item)
    {
        if (item is T found)
        {
            _ = items.Remove(found);
        }
    }

    private static void AttachBlock(Section section, Block block)
    {
        block.Section = section;
        block.SectionId = section.Id;
        section.Blocks.Add(block);
        Workflow.FixBlockChildIds(block);
    }

    private static void AttachSection(Workflow live, Section section)
    {
        section.Workflow = live;
        section.WorkflowId = live.Id;
        live.Sections.Add(section);
        foreach (Block block in section.Blocks)
        {
            block.Section = section;
            block.SectionId = section.Id;
            Workflow.FixBlockChildIds(block);
        }
    }

    private static void MoveBlock(Block block, Section into)
    {
        _ = block.Section!.Blocks.Remove(block);
        AttachBlock(into, block);
    }

    /// <summary>A field's live value as the export spells it; null when absent.</summary>
    private static JToken? ReadField(object target, ProposedChangeScope scope, string field)
    {
        JObject json = ProposalJson.ToJson(target);
        if (scope == ProposedChangeScope.Block && field == ProposalJson.TYPE)
        {
            return new JObject
            {
                [ProposalJson.TYPE] = json[ProposalJson.TYPE]?.DeepClone(),
                [ProposalJson.PROPERTIES] = json[ProposalJson.PROPERTIES]?.DeepClone(),
            };
        }

        if (scope == ProposedChangeScope.Block && field.StartsWith(ProposalJson.PROPERTIES_PREFIX, StringComparison.Ordinal))
        {
            return json[ProposalJson.PROPERTIES]?[field[ProposalJson.PROPERTIES_PREFIX.Length..]];
        }

        return json[field];
    }

    /// <summary>
    /// Overwrites one field. A new value drops its nested row ids so each row the field holds is
    /// a new row; an undo restores the rows as they were.
    /// </summary>
    private static void WriteField(Workflow live, object target, ProposedChangeScope scope, string field, JToken? value, bool stripRowIds)
    {
        JToken written = value is null
            ? JValue.CreateNull()
            : stripRowIds && !field.StartsWith(ProposalJson.PROPERTIES_PREFIX, StringComparison.Ordinal) ? ProposalJson.WithoutRowIds(value) : value.DeepClone();

        if (scope == ProposedChangeScope.Block && field == ProposalJson.TYPE && written is JObject typeAndProperties)
        {
            ProposalJson.Populate(typeAndProperties, target);
        }
        else if (scope == ProposedChangeScope.Block && field.StartsWith(ProposalJson.PROPERTIES_PREFIX, StringComparison.Ordinal))
        {
            ProposalJson.Populate(new JObject { [field[ProposalJson.PROPERTIES_PREFIX.Length..]] = written }, ((Block)target).Properties);
        }
        else
        {
            ProposalJson.Populate(new JObject { [field] = written }, target);
        }

        if (target is Block block)
        {
            Workflow.FixBlockChildIds(block);
        }
        else if (target is Workflow)
        {
            Workflow.FixNavigationProperties(live);
        }
    }

    private sealed class ApplyContext(Workflow live, IReadOnlySet<string> takeProposed, IReadOnlyDictionary<string, string>? mintedKeys)
    {
        public List<ProposalAppliedChange> Applied { get; } = [];

        public List<ProposalConflict> Conflicts { get; } = [];

        public List<ProposalNotApplied> NotApplied { get; } = [];

        public ProposalUndo Undo { get; } = new();

        private readonly Dictionary<string, string> _minted = new(mintedKeys ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        private readonly HashSet<Section> _touched = [];
        private bool _sectionsTouched;

        /// <summary>
        /// Records the order of the sections the changes reach, and of the sections themselves when
        /// a change adds, removes or reorders one, before anything moves.
        /// </summary>
        public void Capture(IReadOnlyList<ProposedChange> changes)
        {
            foreach (ProposedChange change in changes)
            {
                if (change.Scope == ProposedChangeScope.Block)
                {
                    Touch(SectionFor(change.SectionKey));
                    Touch(FindBlock(live, change.TargetKey)?.Section);
                }
                else if (change.Scope == ProposedChangeScope.Section)
                {
                    Touch(SectionFor(change.TargetKey));
                    _sectionsTouched |= change.Kind != ProposedChangeKind.FieldEdited || change.Field == ProposalJson.ORDER;
                }
            }

            foreach (Section section in _touched)
            {
                foreach (Block block in section.Blocks)
                {
                    Undo.BlockOrders[BlockKey(block)] = block.Order;
                }
            }

            if (_sectionsTouched)
            {
                foreach (Section section in live.Sections)
                {
                    Undo.SectionOrders[SectionKey(section)] = section.Order;
                }
            }
        }

        public void Normalize()
        {
            foreach (Section section in _touched.Where(section => live.Sections.Contains(section)))
            {
                section.NormalizeBlockOrdering();
            }

            if (_sectionsTouched)
            {
                live.NormalizeSectionOrdering();
            }
        }

        public void Apply(ProposedChange change)
        {
            switch (change.Kind)
            {
                case ProposedChangeKind.FieldEdited:
                    EditField(change);
                    break;
                case ProposedChangeKind.Added:
                    Add(change);
                    break;
                case ProposedChangeKind.Removed:
                    RemoveNode(change);
                    break;
                case ProposedChangeKind.Moved:
                    Move(change);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown change kind {change.Kind}");
            }
        }

        private void Touch(Section? section)
        {
            if (section is not null)
            {
                _ = _touched.Add(section);
            }
        }

        private void EditField(ProposedChange change)
        {
            object? target = change.Scope == ProposedChangeScope.Section
                ? SectionFor(change.TargetKey)
                : Locate(live, change.Scope, change.TargetKey);
            if (target is null)
            {
                NotApplied.Add(new ProposalNotApplied(change.Id, NOT_FOUND_REASON));
                return;
            }

            JToken? current = ReadField(target, change.Scope, change.Field!);
            JToken? proposed = Parse(change.NewJson);
            if (ProposalJson.Same(current, proposed))
            {
                Applied.Add(new ProposalAppliedChange(change.Id, null));
                return;
            }

            bool orderOnly = change.Field == ProposalJson.ORDER;
            if (!orderOnly && !ProposalJson.Same(current, Parse(change.BaseJson)) && !takeProposed.Contains(change.Id))
            {
                Conflicts.Add(new ProposalConflict(change.Id, change.Field, change.BaseJson, ProposalJson.Text(current), change.NewJson));
                return;
            }

            WriteField(live, target, change.Scope, change.Field!, proposed, stripRowIds: true);
            Undo.Steps.Add(new ProposalUndoStep(ProposalUndoAction.SetField, change.Scope, KeyOf(target, change))
            {
                ChangeId = change.Id,
                Field = change.Field,
                Json = current?.ToString(Formatting.None),
                AppliedJson = ReadField(target, change.Scope, change.Field!)?.ToString(Formatting.None),
            });
            Applied.Add(new ProposalAppliedChange(change.Id, null));
        }

        private void Add(ProposedChange change)
        {
            JToken json = ProposalJson.AsNewNode(ProposalJson.Parse(change.NewJson!));

            switch (change.Scope)
            {
                case ProposedChangeScope.Block:
                    Section? section = SectionFor(change.SectionKey);
                    if (section is null)
                    {
                        NotApplied.Add(new ProposalNotApplied(change.Id, NO_SECTION_REASON));
                        return;
                    }

                    Block block = ProposalJson.Deserialize<Block>(json);
                    block.ReMintStableBlockId();
                    AttachBlock(section, block);
                    Touch(section);
                    Added(change, ProposedChangeScope.Block, block.StableBlockId, block);
                    break;
                case ProposedChangeScope.Section:
                    Section added = ProposalJson.Deserialize<Section>(json);
                    added.ReMintStableSectionId();
                    foreach (Block inside in added.Blocks)
                    {
                        inside.ReMintStableBlockId();
                    }

                    AttachSection(live, added);
                    _minted[change.TargetKey!] = added.StableSectionId!;
                    Added(change, ProposedChangeScope.Section, added.StableSectionId, added);
                    break;
                default:
                    if (FindItem(live, change.Scope, change.TargetKey) is not null)
                    {
                        NotApplied.Add(new ProposalNotApplied(change.Id, ITEM_EXISTS_REASON));
                        return;
                    }

                    AddItem(live, change.Scope, json);
                    Added(change, change.Scope, null, FindItem(live, change.Scope, change.TargetKey)!);
                    break;
            }
        }

        private void Added(ProposedChange change, ProposedChangeScope scope, string? minted, object node)
        {
            Undo.Steps.Add(new ProposalUndoStep(ProposalUndoAction.RemoveAdded, scope, minted ?? change.TargetKey)
            {
                ChangeId = change.Id,
                AppliedJson = Comparable(ProposalJson.ToJson(node)).ToString(Formatting.None),
            });
            Applied.Add(new ProposalAppliedChange(change.Id, minted));
        }

        private void RemoveNode(ProposedChange change)
        {
            object? target = change.Scope == ProposedChangeScope.Section
                ? SectionFor(change.TargetKey)
                : Locate(live, change.Scope, change.TargetKey);
            if (target is null)
            {
                Applied.Add(new ProposalAppliedChange(change.Id, null));
                return;
            }

            JObject current = ProposalJson.ToJson(target);
            JToken comparable = Comparable(current);
            if (change.Scope == ProposedChangeScope.Section && comparable is JObject sectionJson)
            {
                sectionJson[ProposalJson.BLOCKS] ??= new JArray();
            }

            if (!ProposalJson.Same(comparable, Comparable(ProposalJson.Parse(change.BaseJson!))) && !takeProposed.Contains(change.Id))
            {
                if (change.Scope == ProposedChangeScope.Section)
                {
                    NotApplied.Add(new ProposalNotApplied(change.Id, SECTION_NOT_EMPTY_REASON));
                }
                else
                {
                    Conflicts.Add(new ProposalConflict(change.Id, null, change.BaseJson, ProposalJson.Text(comparable), null));
                }

                return;
            }

            Undo.Steps.Add(new ProposalUndoStep(ProposalUndoAction.Restore, change.Scope, KeyOf(target, change))
            {
                ChangeId = change.Id,
                SectionKey = target is Block { Section: Section from } ? SectionKey(from) : null,
                Json = current.ToString(Formatting.None),
            });
            Remove(live, change.Scope, KeyOf(target, change));
            Applied.Add(new ProposalAppliedChange(change.Id, null));
        }

        private void Move(ProposedChange change)
        {
            Block? block = FindBlock(live, change.TargetKey);
            Section? into = SectionFor(ProposalJson.StringOf(ProposalJson.Parse(change.NewJson!)));
            if (block is null)
            {
                NotApplied.Add(new ProposalNotApplied(change.Id, NOT_FOUND_REASON));
                return;
            }

            if (into is null)
            {
                NotApplied.Add(new ProposalNotApplied(change.Id, NO_SECTION_REASON));
                return;
            }

            if (ReferenceEquals(block.Section, into))
            {
                Applied.Add(new ProposalAppliedChange(change.Id, null));
                return;
            }

            string? expected = ProposalJson.StringOf(ProposalJson.Parse(change.BaseJson!));
            Section? from = block.Section;
            string? fromKey = from is null ? null : SectionKey(from);
            if (fromKey != expected && !takeProposed.Contains(change.Id))
            {
                Conflicts.Add(new ProposalConflict(change.Id, null, change.BaseJson, JsonConvert.SerializeObject(fromKey), change.NewJson));
                return;
            }

            Touch(from);
            Touch(into);
            Undo.Steps.Add(new ProposalUndoStep(ProposalUndoAction.MoveBack, ProposedChangeScope.Block, BlockKey(block))
            {
                ChangeId = change.Id,
                SectionKey = fromKey,
                IntoSectionKey = SectionKey(into),
            });
            MoveBlock(block, into);
            Applied.Add(new ProposalAppliedChange(change.Id, null));
        }

        private Section? SectionFor(string? key)
        {
            if (key is not null && _minted.TryGetValue(key, out string? minted))
            {
                key = minted;
            }

            return FindSection(live, key);
        }

        private static string? KeyOf(object target, ProposedChange change)
        {
            return target switch
            {
                Block block => BlockKey(block),
                Section section => SectionKey(section),
                _ => change.TargetKey,
            };
        }
    }
}

public sealed record ProposalApplyResult(
    IReadOnlyList<ProposalAppliedChange> Applied,
    IReadOnlyList<ProposalConflict> Conflicts,
    IReadOnlyList<ProposalNotApplied> NotApplied,
    ProposalUndo Undo);

/// <param name="MintedStableId">The stable id an added block or section received.</param>
public sealed record ProposalAppliedChange(string ChangeId, string? MintedStableId);

/// <summary>
/// The user changed this field during the turn: <paramref name="LiveJson"/> is their value,
/// <paramref name="NewJson"/> the agent's, <paramref name="BaseJson"/> what both started from. For
/// an undo, <paramref name="NewJson"/> is what the apply wrote and <paramref name="BaseJson"/> what
/// the undo would have put back.
/// </summary>
public sealed record ProposalConflict(string ChangeId, string? Field, string? BaseJson, string? LiveJson, string? NewJson);

public sealed record ProposalNotApplied(string ChangeId, string Reason);

/// <summary>The changes an undo left in place because the user changed them after they were applied.</summary>
public sealed record ProposalUndoOutcome(IReadOnlyList<ProposalConflict> Conflicts);

/// <summary>
/// What it takes to put the live workflow back as it was before a proposal's changes: the steps
/// in the order they ran, and the order of the blocks and sections they touched before the first
/// apply.
/// </summary>
public sealed class ProposalUndo
{
    public List<ProposalUndoStep> Steps { get; init; } = [];

    public Dictionary<string, int> BlockOrders { get; init; } = new(StringComparer.Ordinal);

    public Dictionary<string, int> SectionOrders { get; init; } = new(StringComparer.Ordinal);

    public bool IsEmpty => Steps.Count == 0;

    /// <summary>Adds a later apply's steps; the order snapshot keeps the earliest value of each node.</summary>
    public void Merge(ProposalUndo later)
    {
        Steps.AddRange(later.Steps);
        foreach ((string key, int order) in later.BlockOrders)
        {
            _ = BlockOrders.TryAdd(key, order);
        }

        foreach ((string key, int order) in later.SectionOrders)
        {
            _ = SectionOrders.TryAdd(key, order);
        }
    }

    public string ToJson()
    {
        return JsonConvert.SerializeObject(this, ProposalJson.ModelSettings);
    }

    public static ProposalUndo FromJson(string? json)
    {
        return string.IsNullOrWhiteSpace(json)
            ? new ProposalUndo()
            : JsonConvert.DeserializeObject<ProposalUndo>(json, ProposalJson.ModelSettings) ?? new ProposalUndo();
    }
}

public sealed record ProposalUndoStep(ProposalUndoAction Action, ProposedChangeScope Scope, string? Key)
{
    public string ChangeId { get; init; } = string.Empty;

    public string? Field { get; init; }

    public string? SectionKey { get; init; }

    /// <summary>For a move, the section the block was moved into.</summary>
    public string? IntoSectionKey { get; init; }

    /// <summary>The value or node as it was, as JSON; null for a field that was absent.</summary>
    public string? Json { get; init; }

    /// <summary>What the apply left there, to tell a later edit by the user from the apply's own.</summary>
    public string? AppliedJson { get; init; }
}

public enum ProposalUndoAction
{
    SetField = 0,
    RemoveAdded = 1,
    Restore = 2,
    MoveBack = 3,
}

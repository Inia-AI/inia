using System.Globalization;
using IniaAi.Application.Workflows.EditorAgent;
using IniaAi.Common.Serialization;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Proposals;
using IniaAi.WebApp.Components.WorkflowEditor.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block;

namespace IniaAi.WebApp.Components.WorkflowEditor.Agent;

/// <summary>The changes of one block, section or other item, which share a heading on the review card.</summary>
public sealed record EditorAgentChangeGroup(string Key, string Title, string? Subtitle, IReadOnlyList<EditorAgentChangeView> Changes);

/// <param name="CutStart">Unchanged text before the excerpt was left out.</param>
/// <param name="CutEnd">Unchanged text after the excerpt was left out.</param>
public sealed record TextExcerpt(string Before, string After, bool CutStart, bool CutEnd);

/// <summary>Plain names for a proposal's changes: who they touch, what kind they are, and their values.</summary>
public static class EditorAgentChangeText
{
    public const string OUTBOUND = "Sends data to an outside address";
    public const string OUTBOUND_HINT = "Check the address before you accept it. Accept all leaves it out.";
    public const string LIBRARY = "Follows the Library; after saving, this template's copy will differ from it until someone reconciles.";
    public const string NOT_LISTED = "The agent changed this without listing it in its summary.";
    public const string NOT_SET = "(not set)";
    public const string UNKNOWN_CHANGE = "a change";
    public const string ON = "On";
    public const string OFF = "Off";

    private const string UNNAMED_BLOCK = "Unnamed block";
    private const string UNNAMED_SECTION = "Untitled section";
    private const string NEW_SECTION = "a new section";
    private const string WORKFLOW_SETTINGS = "Workflow settings";
    private const string ORDER_FIELD = "Order";
    private const string TYPE_FIELD = "Type";
    private const int EXCERPT_CONTEXT = 160;
    private const int SHORT_VALUE_CHARS = 120;

    private static readonly char[] _wordBreaks = [' ', '\n', '\t'];
    private static readonly string[] _instructionFields = ["UserPromptTemplate", "PromptTemplate", "SystemPromptTemplate"];

    public static IReadOnlyList<EditorAgentChangeGroup> Group(EditorAgentProposalView proposal, Workflow workflow)
    {
        var blocks = new Dictionary<string, Block>(StringComparer.Ordinal);
        foreach (Block block in workflow.GetAllBlocks())
        {
            if (block.StableBlockId is string stableId)
            {
                _ = blocks.TryAdd(stableId, block);
            }
        }

        var groups = new List<EditorAgentChangeGroup>();
        foreach (IGrouping<string, EditorAgentChangeView> group in proposal.Changes.GroupBy(static view => GroupKey(view.Change)))
        {
            ProposedChange first = group.First().Change;
            (string title, string? subtitle) = Heading(first, proposal, workflow, blocks);
            groups.Add(new EditorAgentChangeGroup(group.Key, title, subtitle, [.. group]));
        }

        return groups;
    }

    public static string KindLabel(ProposedChange change)
    {
        string noun = change.Scope switch
        {
            ProposedChangeScope.Block => "Block",
            ProposedChangeScope.Section => "Section",
            ProposedChangeScope.Variable => "Input variable",
            ProposedChangeScope.DocumentClass => "Document type",
            ProposedChangeScope.SignatureField => "Signature field",
            ProposedChangeScope.RegressionField => "Regression field",
            _ => "Setting",
        };

        return change.Kind switch
        {
            ProposedChangeKind.Added => $"{noun} added",
            ProposedChangeKind.Removed => $"{noun} removed",
            ProposedChangeKind.Moved => "Moved",
            _ when WorkflowFieldLabels.IsPrompt(change.Field) => "Prompt",
            _ when WorkflowFieldLabels.IsModelSetting(change.Field) => "Model settings",
            _ when change.Field == TYPE_FIELD => "Block type",
            _ when change.Field == ORDER_FIELD => "Order",
            _ => "Setting",
        };
    }

    /// <summary>The field a field edit changes, in plain words; empty for an added, removed or moved item.</summary>
    public static string FieldLabel(ProposedChange change)
    {
        return change.Kind == ProposedChangeKind.FieldEdited ? WorkflowFieldLabels.Label(change.Field) : string.Empty;
    }

    /// <summary>
    /// A value in plain words: a text field's text as it reads, a switch as On or Off, a list as its
    /// items, and a group of settings as one line per setting.
    /// </summary>
    public static string? ValueText(string? json)
    {
        if (json is null)
        {
            return null;
        }

        try
        {
            return Plain(JsonText.Parse(json));
        }
        catch (JsonReaderException)
        {
            return json;
        }
    }

    private static string? Plain(JToken token)
    {
        switch (token)
        {
            case { Type: JTokenType.Null }:
                return null;
            case JValue { Type: JTokenType.Boolean } flag:
                return (bool)flag ? ON : OFF;
            case JValue value:
                return value.ToString(CultureInfo.InvariantCulture);
            case JArray list when list.All(static item => item is JValue):
                return list.Count == 0 ? NOT_SET : string.Join(", ", list.Select(static item => Plain(item) ?? NOT_SET));
            case JArray list:
                return list.Count == 1 ? "1 item" : $"{list.Count} items";
            case JObject settings:
                return string.Join('\n', settings.Properties()
                    .Select(static setting => $"{WorkflowFieldLabels.Label(setting.Name)}: {Nested(setting.Value)}"));
            default:
                return token.ToString();
        }
    }

    private static string Nested(JToken token)
    {
        return token is JObject ? "several settings" : Plain(token) ?? NOT_SET;
    }

    /// <summary>
    /// The part of a long text that changed, with some unchanged words either side, so a one-line
    /// edit in a long prompt shows where it is without the whole prompt.
    /// </summary>
    public static TextExcerpt Excerpt(string? before, string? after)
    {
        before ??= string.Empty;
        after ??= string.Empty;

        int shorter = Math.Min(before.Length, after.Length);
        int prefix = 0;
        while (prefix < shorter && before[prefix] == after[prefix])
        {
            prefix++;
        }

        int suffix = 0;
        while (suffix < shorter - prefix && before[^(suffix + 1)] == after[^(suffix + 1)])
        {
            suffix++;
        }

        int start = Math.Max(0, prefix - EXCERPT_CONTEXT);
        if (start > 0)
        {
            int space = before.IndexOfAny(_wordBreaks, start);
            start = space >= 0 && space < prefix ? space + 1 : start;
        }

        int keptTail = Math.Min(suffix, EXCERPT_CONTEXT);
        int beforeEnd = before.Length - suffix + keptTail;
        int afterEnd = after.Length - suffix + keptTail;
        if (beforeEnd < before.Length)
        {
            int space = before.LastIndexOfAny(_wordBreaks, beforeEnd - 1);
            if (space > before.Length - suffix)
            {
                afterEnd -= beforeEnd - space;
                beforeEnd = space;
            }
        }

        return new TextExcerpt(before[start..beforeEnd], after[start..afterEnd], start > 0, beforeEnd < before.Length);
    }

    /// <summary>
    /// True for a short value that is not a prompt, such as a name or a setting: it reads best as
    /// the old value, an arrow and the new one, not word by word.
    /// </summary>
    public static bool IsShortValue(ProposedChange change)
    {
        if (WorkflowFieldLabels.IsPrompt(change.Field))
        {
            return false;
        }

        string? before = ValueText(change.BaseJson);
        string? after = ValueText(change.NewJson);
        return (before ?? string.Empty).Length + (after ?? string.Empty).Length <= SHORT_VALUE_CHARS
            && before?.Contains('\n') != true
            && after?.Contains('\n') != true;
    }

    /// <summary>True when both sides read as running text, so a word-level highlight helps.</summary>
    public static bool IsText(ProposedChange change)
    {
        return IsTextJson(change.BaseJson) && IsTextJson(change.NewJson)
            && (change.BaseJson is not null || change.NewJson is not null);
    }

    public static string MovedText(ProposedChange change, EditorAgentProposalView proposal, Workflow workflow)
    {
        string from = SectionTitle(ValueText(change.BaseJson), proposal, workflow);
        string to = SectionTitle(ValueText(change.NewJson), proposal, workflow);
        return $"From section \"{from}\" to section \"{to}\".";
    }

    public static string AddedText(ProposedChange change, EditorAgentProposalView proposal, Workflow workflow)
    {
        return change.Scope switch
        {
            ProposedChangeScope.Block => $"Adds the {TypeName(change.BlockType)} block \"{change.Name ?? change.Tag ?? UNNAMED_BLOCK}\""
                + $"{(change.Tag is null ? string.Empty : $" (tag {change.Tag})")} to section \"{SectionTitle(change.SectionKey, proposal, workflow)}\".",
            ProposedChangeScope.Section => $"Adds the section \"{change.Name ?? UNNAMED_SECTION}\".",
            _ => $"Adds \"{change.Name ?? change.TargetKey}\".",
        };
    }

    /// <summary>The instructions of an added block, so its card shows what the block will ask; null when it has none.</summary>
    public static string? AddedInstructions(ProposedChange change)
    {
        if (change is not { Scope: ProposedChangeScope.Block, Kind: ProposedChangeKind.Added, NewJson: string json })
        {
            return null;
        }

        return InstructionsOf(JsonText.Parse(json)["Properties"]);
    }

    /// <summary>A block's instructions from its <c>Properties</c> JSON; null when it has none.</summary>
    public static string? InstructionsOf(JToken? properties)
    {
        string? instructions = _instructionFields
            .Select(field => properties?[field] is JValue { Type: JTokenType.String } value ? (string?)value : null)
            .FirstOrDefault(static text => !string.IsNullOrWhiteSpace(text));
        return instructions?.Trim();
    }

    public static string RemovedText(ProposedChange change)
    {
        return change.Scope switch
        {
            ProposedChangeScope.Block => $"Removes the block \"{change.Name ?? change.Tag ?? UNNAMED_BLOCK}\" and its settings.",
            ProposedChangeScope.Section => $"Removes the section \"{change.Name ?? UNNAMED_SECTION}\".",
            _ => $"Removes \"{change.Name ?? change.TargetKey}\".",
        };
    }

    public static string CheckLabel(string check)
    {
        return check switch
        {
            ProposalChecks.FILES => "The agent's files",
            ProposalChecks.SCHEMA => "Workflow format",
            ProposalChecks.STRUCTURE => "Workflow structure",
            ProposalChecks.LIBRARY => "Library links",
            ProposalChecks.AGENT_TAGS => "Agent block tags",
            ProposalChecks.AGENT_GATES => "Agent block gates",
            ProposalChecks.SIGNATURE_FIELDS => "Signature fields",
            ProposalChecks.REPORT_TEXT => "Text from a client report",
            _ => check,
        };
    }

    /// <summary>The block, section or item a change names, for an accessible button name.</summary>
    public static string Subject(ProposedChange change, EditorAgentProposalView proposal, Workflow workflow)
    {
        return Subject(change, Heading(change, proposal, workflow, blocks: null).Title);
    }

    /// <summary>The same, for a change of a group whose title is already known.</summary>
    public static string Subject(ProposedChange change, string owner)
    {
        string field = FieldLabel(change);
        return string.IsNullOrEmpty(field) ? $"{KindLabel(change)}, {owner}" : $"{field} of {owner}";
    }

    /// <summary>
    /// The stable ids in the editor that an applied change touched, for the changed-by-the-agent
    /// mark. Renumbering marks nothing: the block or section did not change.
    /// </summary>
    public static string? ChangedStableId(ProposedChange change, string? mintedStableId)
    {
        if (change.Kind == ProposedChangeKind.Removed || change.Incidental)
        {
            return null;
        }

        return change.Scope is ProposedChangeScope.Block or ProposedChangeScope.Section
            ? mintedStableId ?? change.TargetKey
            : null;
    }

    public static string BlockSubtitle(Block block)
    {
        string section = Blank(block.Section?.Title) ?? Blank(block.Section?.Name) ?? UNNAMED_SECTION;
        return $"{BlockTypeVisuals.Label(block.Type)} block in section \"{section}\"";
    }

    /// <summary>The group a change belongs to: one per block, section or other item.</summary>
    public static string GroupKey(ProposedChange change)
    {
        return $"{change.Scope}:{change.TargetKey}";
    }

    /// <param name="blocks">The workflow's blocks by stable id; null looks the block up in the workflow.</param>
    private static (string Title, string? Subtitle) Heading(
        ProposedChange change,
        EditorAgentProposalView proposal,
        Workflow workflow,
        IReadOnlyDictionary<string, Block>? blocks)
    {
        switch (change.Scope)
        {
            case ProposedChangeScope.Block:
                Block? live = change.TargetKey is null
                    ? null
                    : blocks is null
                        ? workflow.GetAllBlocks().FirstOrDefault(block => block.StableBlockId == change.TargetKey)
                        : blocks.GetValueOrDefault(change.TargetKey);
                string name = Blank(change.Name) ?? Blank(live?.Name) ?? change.Tag ?? live?.ReplacementTag ?? UNNAMED_BLOCK;
                string sectionKey = change.SectionKey ?? live?.Section?.StableSectionId ?? string.Empty;
                return (name, $"{TypeName(change.BlockType)} block in section \"{SectionTitle(sectionKey, proposal, workflow)}\"");
            case ProposedChangeScope.Section:
                return ($"Section \"{SectionTitle(change.TargetKey, proposal, workflow)}\"", null);
            case ProposedChangeScope.Variable:
                return ($"Input variable \"{change.Name ?? change.TargetKey}\"", null);
            case ProposedChangeScope.DocumentClass:
                return ($"Document type \"{change.Name ?? change.TargetKey}\"", null);
            case ProposedChangeScope.SignatureField:
                return ($"Signature field \"{change.Name ?? change.TargetKey}\"", null);
            case ProposedChangeScope.RegressionField:
                return ($"Regression field \"{change.Name ?? change.TargetKey}\"", null);
            default:
                return (WORKFLOW_SETTINGS, null);
        }
    }

    private static string SectionTitle(string? sectionKey, EditorAgentProposalView proposal, Workflow workflow)
    {
        if (string.IsNullOrEmpty(sectionKey))
        {
            return UNNAMED_SECTION;
        }

        Section? live = workflow.Sections.FirstOrDefault(section => section.StableSectionId == sectionKey);
        if (live is not null)
        {
            return Blank(live.Title) ?? Blank(live.Name) ?? UNNAMED_SECTION;
        }

        string? proposed = proposal.Changes
            .Select(static view => view.Change)
            .FirstOrDefault(change => change.Scope == ProposedChangeScope.Section && change.TargetKey == sectionKey)?.Name;

        return Blank(proposed) ?? NEW_SECTION;
    }

    private static string TypeName(string? blockType)
    {
        return Enum.TryParse(blockType, out BlockTypes type) ? BlockTypeVisuals.Label(type) : blockType ?? string.Empty;
    }

    private static bool IsTextJson(string? json)
    {
        if (json is null)
        {
            return true;
        }

        try
        {
            return JsonText.Parse(json).Type is JTokenType.String or JTokenType.Null;
        }
        catch (JsonReaderException)
        {
            return false;
        }
    }

    private static string? Blank(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}

using System.Globalization;
using System.Text;
using System.Text.Json;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Common.Resources;
using IniaAi.Core.Workflows.Dependencies;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.JsonSchemas;
using IniaAi.WebApi.Contracts.Responses.WorkflowVersions;
using Newtonsoft.Json.Linq;

namespace IniaAi.Application.Workflows.EditorAgent;

/// <summary>
/// What one editor agent turn stages: the files the agent reads, the guidance skill, the system
/// prompt and the turn's message. Pure functions of the draft, so a test can stage any workflow.
/// </summary>
public static class EditorAgentStaging
{
    public const string SKILL_NAME = "workflow-guide";
    public const string WORKFLOW_DOCUMENT = "workflow.json";
    public const string OUTLINE_DOCUMENT = "outline.txt";
    public const string DEPENDENCY_MAP_DOCUMENT = "dependency-map.json";
    public const string VERSIONS_DOCUMENT = "versions.md";
    public const string BLOCK_LINK_SCHEME = "block:";

    public const string SCHEMA_ASSET = "workflow.schema.json";
    public const string CHECKER_ASSET = "check_workflow.py";
    public const string HELPER_ASSET = "wf.py";
    public const string FEATURES_ASSET = "features.md";

    private const string SKILL_DESCRIPTION =
        "How to read the open workflow from the staged files and propose changes to it: what each file holds, "
        + "how sections, blocks, tags, variables, document classes and Library links work, the wf.py lookup helper, "
        + "the editing rules, the schema checker, and how to write the answer. Load it before the first answer "
        + "in a conversation.";
    private const string SYSTEM_PROMPT_RESOURCE = "editor-agent-system-prompt.md";
    private const string GUIDE_RESOURCE = "editor-agent-workflow-guide.md";
    private const string UNCHANGED_NOTE_RESOURCE = "editor-agent-note-unchanged.md";
    private const string CHANGED_NOTE_RESOURCE = "editor-agent-note-changed.md";
    private const string FEATURES_RESOURCE = "editor-agent-platform-features.md";
    private const string JSON_MEDIA_TYPE = "application/json";
    private const string TEXT_MEDIA_TYPE = "text/plain";
    private const string MARKDOWN_MEDIA_TYPE = "text/markdown";
    private const string LIBRARY_MARK = ", Library";
    private const string SECTION_LIBRARY_MARK = "  (Library)";
    private const string NO_TAG = "(no tag)";
    private const string NO_ID = "no id";
    private const string VARIABLES_HEADER = "Variables:";
    private const string NOTES_HEADER = "Workflow notes (what it is for and how it should work; keep them current):";
    private const string NO_NOTES_LINE = "  (none yet: write them in your first proposal, from the workflow and its reports)";
    private const string DOCUMENT_CLASSES_HEADER = "Document classes:";
    private const string SECTIONS_HEADER = "Sections and blocks, in execution order:";
    private const string NONE_LINE = "  (none)";
    private const string VERSIONS_TITLE = "# Saved versions, newest first";
    private const string NO_VERSIONS_LINE = "This workflow has no saved versions yet.";
    private const string MORE_VERSIONS_LINE = "Older versions exist but are not listed.";
    private const string NO_SUMMARY = "no summary";
    private const string ADDED = "added";
    private const string REMOVED = "removed";
    private const string CHANGED = "changed";
    private const string NOTE_SEPARATOR = "\n\n---\n";
    private const string PICKED_REPORTS_HEADER =
        "The user picked these past reports as examples for this message, each staged as a header and its run file. "
        + "Each line says whether the report is a run of the open workflow:";
    private const string NO_COUNTERPARTY = "no counterparty";
    private const string SECTIONS_KEY = "Sections";
    private const string BLOCKS_KEY = "Blocks";
    private const string STABLE_ID_KEY = "StableBlockId";
    private const string NAME_KEY = "Name";
    private const string TAG_KEY = "ReplacementTag";

    private static readonly Lazy<string> _systemPrompt = new(() => ReadResource(SYSTEM_PROMPT_RESOURCE));
    private static readonly Lazy<string> _guide = new(() => ReadResource(GUIDE_RESOURCE));
    private static readonly Lazy<string> _unchangedNote = new(() => ReadResource(UNCHANGED_NOTE_RESOURCE).Trim());
    private static readonly Lazy<string> _changedNote = new(() => ReadResource(CHANGED_NOTE_RESOURCE).Trim());
    private static readonly Lazy<string> _checkerScript = new(() => ReadResource(CHECKER_ASSET));
    private static readonly Lazy<string> _helperScript = new(() => ReadResource(HELPER_ASSET));
    private static readonly Lazy<string> _features = new(() => ReadResource(FEATURES_RESOURCE));

    private static readonly JsonSerializerOptions _mapJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string SystemPrompt => _systemPrompt.Value;

    /// <summary>
    /// A dependency-free Python checker for the workflow JSON Schema, run inside the agent's
    /// sandbox, which has no JSON Schema library. It understands only
    /// <see cref="WorkflowJsonSchema.CheckerKeywords"/>.
    /// </summary>
    public static string CheckerScript => _checkerScript.Value;

    /// <summary>
    /// The lookup helper the agent runs instead of ad-hoc scripts: it loads the staged files once
    /// per call and prints compact answers, so one tool call answers what took several.
    /// </summary>
    public static string HelperScript => _helperScript.Value;

    /// <summary>
    /// The guidance skill, with the workflow JSON Schema and its checker as read-only assets the
    /// agent runs before it proposes.
    /// </summary>
    public static AgentRunSkillFile GuideSkill()
    {
        return new AgentRunSkillFile(SKILL_NAME, _guide.Value, SKILL_DESCRIPTION)
        {
            Assets =
            [
                new AgentRunSkillAsset(HELPER_ASSET, HelperScript),
                new AgentRunSkillAsset(CHECKER_ASSET, CheckerScript),
                new AgentRunSkillAsset(SCHEMA_ASSET, WorkflowJsonSchema.Json),
                new AgentRunSkillAsset(FEATURES_ASSET, _features.Value),
            ],
        };
    }

    public static IReadOnlyList<AgentRunDocument> Documents(
        string workflowJson,
        Workflow workflow,
        IReadOnlyList<WorkflowVersionSummaryDto> versions,
        bool moreVersions)
    {
        return
        [
            Document(OUTLINE_DOCUMENT, Outline(workflow), TEXT_MEDIA_TYPE),
            Document(WORKFLOW_DOCUMENT, workflowJson, JSON_MEDIA_TYPE),
            Document(DEPENDENCY_MAP_DOCUMENT, DependencyMapJson(workflow), JSON_MEDIA_TYPE),
            Document(VERSIONS_DOCUMENT, VersionsMarkdown(versions, moreVersions), MARKDOWN_MEDIA_TYPE),
        ];
    }

    /// <summary>
    /// The workflow's notes first, then one line per variable, document class, section and block. Blocks are listed by
    /// <c>Order</c>, which is execution order, never by their position in the array.
    /// </summary>
    public static string Outline(Workflow workflow)
    {
        var builder = new StringBuilder();

        _ = builder.AppendLine(NOTES_HEADER)
            .AppendLine(string.IsNullOrWhiteSpace(workflow.Notes) ? NO_NOTES_LINE : workflow.Notes.Trim())
            .AppendLine();

        _ = builder.AppendLine(VARIABLES_HEADER);
        if (workflow.Variables.Count == 0)
        {
            _ = builder.AppendLine(NONE_LINE);
        }

        foreach (WorkflowVariable variable in workflow.Variables.OrderBy(static v => v.ReplacementTag, StringComparer.Ordinal))
        {
            _ = builder.AppendLine(CultureInfo.InvariantCulture, $"  {{{variable.ReplacementTag}}}  {variable.Type}  {variable.DisplayName}");
        }

        _ = builder.AppendLine().AppendLine(DOCUMENT_CLASSES_HEADER);
        if (workflow.DocumentClasses.Count == 0)
        {
            _ = builder.AppendLine(NONE_LINE);
        }

        foreach (DocumentClass documentClass in workflow.DocumentClasses.OrderBy(static d => d.Key, StringComparer.Ordinal))
        {
            _ = builder.AppendLine(CultureInfo.InvariantCulture, $"  {documentClass.Key}  {documentClass.DisplayName}");
        }

        _ = builder.AppendLine().AppendLine(SECTIONS_HEADER);
        foreach (Section section in workflow.Sections.OrderBy(static s => s.Order))
        {
            string sectionLibrary = section.SharedComponentId is null ? string.Empty : SECTION_LIBRARY_MARK;
            string title = string.IsNullOrWhiteSpace(section.Title) ? section.Name : section.Title;
            _ = builder.AppendLine(CultureInfo.InvariantCulture, $"{section.Order}. {title}{sectionLibrary}");

            int position = 0;
            foreach (Block block in section.Blocks.OrderBy(static b => b.Order))
            {
                string tag = string.IsNullOrWhiteSpace(block.ReplacementTag) ? NO_TAG : block.ReplacementTag;
                string blockLibrary = block.SharedComponentId is null ? string.Empty : LIBRARY_MARK;
                _ = builder.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"{section.Order}.{position}  [{block.Type}] {tag}  {block.Name}  (id {block.StableBlockId ?? NO_ID}{blockLibrary})");
                position++;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// The dependency map with blocks named by StableBlockId, the id the agent links answers by.
    /// Inputs keep the map's own ids.
    /// </summary>
    public static string DependencyMapJson(Workflow workflow)
    {
        WorkflowDependencyMap map = WorkflowDependencyMap.ForTemplate(workflow);
        Dictionary<string, string> stableIds = map.Blocks.ToDictionary(
            static block => block.Id,
            static block => block.Block.StableBlockId ?? block.Id,
            StringComparer.Ordinal);
        Dictionary<string, int> sectionOrders = workflow.Sections
            .SelectMany(static section => section.Blocks.Select(block => (block.Id, section.Order)))
            .GroupBy(static pair => pair.Id, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First().Order, StringComparer.Ordinal);

        string NodeId(string id)
        {
            return stableIds.TryGetValue(id, out string? stableId) ? stableId : id;
        }

        var document = new
        {
            Blocks = map.Blocks.Select(block => new
            {
                Id = NodeId(block.Id),
                block.Block.Name,
                Tag = block.Block.ReplacementTag,
                Type = block.Block.Type.ToString(),
                Section = sectionOrders.TryGetValue(block.Id, out int order) ? order : (int?)null,
                Variants = block.VariantKeys,
            }),
            Inputs = map.Inputs.Select(static input => new
            {
                input.Id,
                Kind = input.Kind.ToString(),
                input.Tag,
            }),
            Edges = map.Edges.Select(edge => new
            {
                From = NodeId(edge.SourceId),
                To = NodeId(edge.TargetId),
                Kind = edge.Kind.ToString(),
                edge.Tags,
                Variants = edge.VariantKeys,
            }),
            VerificationGroups = map.VerificationGroups.Select(group => new
            {
                Verifier = NodeId(group.VerifierBlockId),
                Members = group.MemberBlockIds.Select(NodeId),
            }),
            Issues = map.Issues.Select(issue => new
            {
                Kind = issue.Kind.ToString(),
                Blocks = issue.BlockIds.Select(NodeId),
                issue.Tag,
            }),
        };

        return JsonSerializer.Serialize(document, _mapJsonOptions);
    }

    public static string VersionsMarkdown(IReadOnlyList<WorkflowVersionSummaryDto> versions, bool moreVersions)
    {
        var builder = new StringBuilder();
        _ = builder.AppendLine(VERSIONS_TITLE).AppendLine();

        if (versions.Count == 0)
        {
            _ = builder.AppendLine(NO_VERSIONS_LINE);
        }

        foreach (WorkflowVersionSummaryDto version in versions)
        {
            string summary = string.IsNullOrWhiteSpace(version.Narrative) ? NO_SUMMARY : version.Narrative.Trim();
            _ = builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"- {version.CreatedAtUtc:yyyy-MM-dd HH:mm} UTC, {version.CreatedByName} (version {version.ShortCode}): {summary}");
        }

        if (moreVersions)
        {
            _ = builder.AppendLine().AppendLine(MORE_VERSIONS_LINE);
        }

        return builder.ToString();
    }

    /// <summary>
    /// The blocks whose content differs between two drafts, paired by StableBlockId. Test-run
    /// outputs count: a block re-tested since the last answer has new evidence to read.
    /// </summary>
    public static IReadOnlyList<EditorAgentBlockChange> ChangedBlocks(string previousJson, string currentJson)
    {
        Dictionary<string, JObject> previous = BlocksById(previousJson);
        Dictionary<string, JObject> current = BlocksById(currentJson);
        var changes = new List<EditorAgentBlockChange>();

        foreach ((string id, JObject block) in current)
        {
            if (!previous.TryGetValue(id, out JObject? before))
            {
                changes.Add(ChangeOf(id, block, ADDED));
            }
            else if (!JToken.DeepEquals(before, block))
            {
                changes.Add(ChangeOf(id, block, CHANGED));
            }
        }

        foreach ((string id, JObject block) in previous)
        {
            if (!current.ContainsKey(id))
            {
                changes.Add(ChangeOf(id, block, REMOVED));
            }
        }

        return changes;
    }

    /// <summary>
    /// The user's message, followed by the platform's note on what changed since the previous
    /// turn. The first turn carries no note.
    /// </summary>
    public static string TurnPrompt(string userMessage, IReadOnlyList<EditorAgentBlockChange>? changes)
    {
        if (changes is null)
        {
            return userMessage;
        }

        var builder = new StringBuilder(userMessage);
        _ = builder.Append(NOTE_SEPARATOR);

        if (changes.Count == 0)
        {
            _ = builder.Append(_unchangedNote.Value);
            return builder.ToString();
        }

        _ = builder.AppendLine(_changedNote.Value);
        foreach (EditorAgentBlockChange change in changes)
        {
            _ = builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"- {change.Kind}: [{change.Name}]({BLOCK_LINK_SCHEME}{change.StableBlockId}) ({change.Tag})");
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// The platform's note naming the reports the user picked for this message, after the
    /// message; empty when none were picked.
    /// </summary>
    public static string PickedReportsNote(IReadOnlyList<EditorAgentReportSummary> picked, EditorAgentOpenWorkflowType? openWorkflowType = null)
    {
        if (picked.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(NOTE_SEPARATOR);
        _ = builder.AppendLine(PICKED_REPORTS_HEADER);
        foreach (EditorAgentReportSummary report in picked)
        {
            _ = builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"- {EditorAgentReports.HeaderDocumentName(report.Id)}: {report.WorkflowTypeName}{EditorAgentReports.TypeMark(report, openWorkflowType)}; {report.Date:yyyy-MM-dd}, {report.Counterparty ?? NO_COUNTERPARTY}, {report.Status}");
        }

        return builder.ToString().TrimEnd();
    }

    private static Dictionary<string, JObject> BlocksById(string workflowJson)
    {
        var blocks = new Dictionary<string, JObject>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(workflowJson))
        {
            return blocks;
        }

        JObject root = JObject.Parse(workflowJson);
        if (root[SECTIONS_KEY] is not JArray sections)
        {
            return blocks;
        }

        foreach (JObject block in sections.OfType<JObject>()
            .SelectMany(static section => section[BLOCKS_KEY] as JArray ?? [])
            .OfType<JObject>())
        {
            if (block[STABLE_ID_KEY]?.Value<string>() is string id)
            {
                blocks[id] = block;
            }
        }

        return blocks;
    }

    private static EditorAgentBlockChange ChangeOf(string id, JObject block, string kind)
    {
        return new EditorAgentBlockChange(
            id,
            block[NAME_KEY]?.Value<string>() ?? string.Empty,
            block[TAG_KEY]?.Value<string>() ?? string.Empty,
            kind);
    }

    private static AgentRunDocument Document(string name, string content, string mediaType)
    {
        return new AgentRunDocument(name, Encoding.UTF8.GetBytes(content), mediaType);
    }

    private static string ReadResource(string name)
    {
        using var reader = new StreamReader(typeof(EditorAgentStaging).Assembly.ReadResource(name));
        return reader.ReadToEnd();
    }
}

/// <param name="Kind">added, removed or changed.</param>
public sealed record EditorAgentBlockChange(string StableBlockId, string Name, string Tag, string Kind);

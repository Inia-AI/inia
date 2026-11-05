using System.Text;
using System.Text.Json;
using IniaAi.Application.Workflows.EditorAgent;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Domain.JsonSchemas;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.WebApi.Contracts.Responses.WorkflowVersions;

namespace IniaAi.Application.UnitTests.Workflows.EditorAgent;

public class EditorAgentStagingTests
{
    [Fact]
    public void Outline_ListsBlocksInExecutionOrderWithTypeTagNameAndStableId()
    {
        Workflow workflow = EditorAgentWorkflows.Build();
        Block producer = EditorAgentWorkflows.Producer(workflow);
        Block reader = EditorAgentWorkflows.Reader(workflow);

        string[] lines = EditorAgentStaging.Outline(workflow).Split('\n', StringSplitOptions.TrimEntries);

        int producerLine = Array.FindIndex(lines, line => line.Contains(producer.StableBlockId!, StringComparison.Ordinal));
        int readerLine = Array.FindIndex(lines, line => line.Contains(reader.StableBlockId!, StringComparison.Ordinal));
        Assert.True(producerLine >= 0 && readerLine > producerLine, "the producer runs first, so it is listed first");
        Assert.Equal(
            $"0.0  [AiQuery] {EditorAgentWorkflows.PRODUCER_TAG}  {EditorAgentWorkflows.PRODUCER_NAME}  (id {producer.StableBlockId})",
            lines[producerLine]);
        Assert.Contains(lines, line => line.StartsWith("{" + EditorAgentWorkflows.VARIABLE_TAG + "}", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith(EditorAgentWorkflows.DOCUMENT_CLASS, StringComparison.Ordinal));
    }

    [Fact]
    public void Outline_StartsWithTheWorkflowNotes_OrAsksForThemWhenThereAreNone()
    {
        Workflow workflow = EditorAgentWorkflows.Build();
        string empty = EditorAgentStaging.Outline(workflow);
        workflow.Notes = "A credit report for a bank's committee.\nFigures are in millions.";

        string[] lines = EditorAgentStaging.Outline(workflow).Split('\n', StringSplitOptions.TrimEntries);

        Assert.StartsWith("Workflow notes", lines[0], StringComparison.Ordinal);
        Assert.Equal("A credit report for a bank's committee.", lines[1]);
        Assert.Equal("Figures are in millions.", lines[2]);
        Assert.Contains("(none yet: write them in your first proposal", empty, StringComparison.Ordinal);
    }

    [Fact]
    public void Outline_MarksALibraryLinkedBlock()
    {
        Workflow workflow = EditorAgentWorkflows.Build();
        EditorAgentWorkflows.Producer(workflow).SharedComponentId = "component-1";

        string outline = EditorAgentStaging.Outline(workflow);

        Assert.Contains($"{EditorAgentWorkflows.Producer(workflow).StableBlockId}, Library)", outline);
    }

    /// <summary>
    /// "What reads Y" is answered from the map's edges, so an edge must name both blocks by the
    /// StableBlockId the agent links its answer with.
    /// </summary>
    [Fact]
    public void DependencyMapJson_NamesEdgesByStableBlockId()
    {
        Workflow workflow = EditorAgentWorkflows.Build();
        string producer = EditorAgentWorkflows.Producer(workflow).StableBlockId!;
        string reader = EditorAgentWorkflows.Reader(workflow).StableBlockId!;

        using JsonDocument map = JsonDocument.Parse(EditorAgentStaging.DependencyMapJson(workflow));

        Assert.Contains(
            map.RootElement.GetProperty("edges").EnumerateArray(),
            edge => edge.GetProperty("from").GetString() == producer
                && edge.GetProperty("to").GetString() == reader
                && edge.GetProperty("kind").GetString() == "Data");
        Assert.Contains(
            map.RootElement.GetProperty("blocks").EnumerateArray(),
            block => block.GetProperty("id").GetString() == reader
                && block.GetProperty("tag").GetString() == EditorAgentWorkflows.READER_TAG);
    }

    [Fact]
    public void ChangedBlocks_PairsByStableBlockId()
    {
        Workflow before = EditorAgentWorkflows.Build();
        string beforeJson = before.ToExportJson(includeRunTraces: true);

        Workflow after = Workflow.FromExportJson(beforeJson)!;
        Block reader = EditorAgentWorkflows.Reader(after);
        ((AiQueryProperties)reader.Properties).UserPromptTemplate = "Summarize briefly: {revenue.answer}";
        after.Sections.Single().Blocks.Remove(EditorAgentWorkflows.Producer(after));

        IReadOnlyList<EditorAgentBlockChange> changes =
            EditorAgentStaging.ChangedBlocks(beforeJson, after.ToExportJson(includeRunTraces: true));

        Assert.Equal(2, changes.Count);
        Assert.Contains(changes, change => change.StableBlockId == reader.StableBlockId && change.Kind == "changed");
        Assert.Contains(
            changes,
            change => change.StableBlockId == EditorAgentWorkflows.Producer(before).StableBlockId && change.Kind == "removed");
    }

    [Fact]
    public void TurnPrompt_FirstTurnHasNoNote_LaterTurnsSayWhatChanged()
    {
        Assert.Equal("Where does revenue come from?", EditorAgentStaging.TurnPrompt("Where does revenue come from?", null));
        Assert.EndsWith(
            "The workflow has not changed since your last answer.",
            EditorAgentStaging.TurnPrompt("And now?", []));

        string prompt = EditorAgentStaging.TurnPrompt(
            "And now?",
            [new EditorAgentBlockChange("stable-1", "Summary", "summary", "changed")]);

        Assert.StartsWith("And now?", prompt);
        Assert.Contains("- changed: [Summary](block:stable-1) (summary)", prompt);
    }

    [Fact]
    public void Documents_AreTheFourStagedFilesUnderAdmissibleNames()
    {
        Workflow workflow = EditorAgentWorkflows.Build();
        string json = workflow.ToExportJson(includeRunTraces: true);
        var version = new WorkflowVersionSummaryDto(
            "v1", new DateTime(2026, 10, 1, 9, 30, 0, DateTimeKind.Utc), "u1", "Jane Doe", "ab12cd", new string('a', 64),
            "Changed the revenue prompt.", null);

        IReadOnlyList<AgentRunDocument> documents = EditorAgentStaging.Documents(json, workflow, [version], moreVersions: false);

        Assert.Equal(
            [
                EditorAgentStaging.OUTLINE_DOCUMENT,
                EditorAgentStaging.WORKFLOW_DOCUMENT,
                EditorAgentStaging.DEPENDENCY_MAP_DOCUMENT,
                EditorAgentStaging.VERSIONS_DOCUMENT,
            ],
            documents.Select(static document => document.Name));
        Assert.All(documents, document => Assert.Equal(document.Name, AgentStagedNames.Stage(document.Name, [])));
        Assert.Equal(json, Encoding.UTF8.GetString(documents[1].Content.Span));
        Assert.Contains(
            "- 2026-10-01 09:30 UTC, Jane Doe (version ab12cd): Changed the revenue prompt.",
            Encoding.UTF8.GetString(documents[3].Content.Span));
    }

    [Fact]
    public void GuideSkill_IsAdmissible_ShipsTheSchemaAndItsChecker_AndCarriesThePromptConventions()
    {
        AgentRunSkillFile skill = EditorAgentStaging.GuideSkill();

        Assert.Null(AgentSkillNames.DescribeFirstProblem([(skill.Name, skill.Description!)]));
        Assert.Null(AgentSkillAssets.DescribeFirstProblem(skill.Name, skill.Assets.Select(static asset => (asset.Name, asset.Content))));
        Assert.Equal(
            [EditorAgentStaging.HELPER_ASSET, EditorAgentStaging.CHECKER_ASSET, EditorAgentStaging.SCHEMA_ASSET, EditorAgentStaging.FEATURES_ASSET],
            skill.Assets.Select(static asset => asset.Name));
        Assert.Equal(WorkflowJsonSchema.Json, skill.Assets.Single(asset => asset.Name == EditorAgentStaging.SCHEMA_ASSET).Content);
        Assert.Contains("def check(", EditorAgentStaging.CheckerScript, StringComparison.Ordinal);
        Assert.Equal(EditorAgentStaging.CheckerScript, skill.Assets.Single(asset => asset.Name == EditorAgentStaging.CHECKER_ASSET).Content);
        Assert.Contains("check_workflow.py\" out/workflow.json", skill.Content);
        Assert.Contains("docs/outline.txt", skill.Content);
        Assert.Contains("report-<id>.workflow.json", skill.Content);
        Assert.Contains("TemplateStableBlockId", skill.Content);
        Assert.Contains("## Prompt conventions", skill.Content);
        Assert.Contains("before the answer schema", skill.Content);
        Assert.Contains("`{tag.answer}`", skill.Content);
        Assert.Contains("WorkflowTemplatesAgentGuide.md", skill.Content);
        Assert.Contains("## Editing", skill.Content);
        Assert.Contains("out/summary.json", skill.Content);
        Assert.Contains("\"StableBlockId\": \"new-<short-name>\"", skill.Content);
    }

    [Fact]
    public void SystemPrompt_SaysTheAgentProposesButNeverSavesOrRuns_AndLinksBlocks()
    {
        string prompt = EditorAgentStaging.SystemPrompt;

        Assert.Contains("Never save or run anything", prompt);
        Assert.Contains("Never copy client report content into the workflow", prompt);
        Assert.Contains("never state a cause as fact", prompt);
        Assert.Contains("say block by block what you changed and why", prompt);
        Assert.Contains("[Block name](block:<StableBlockId>)", prompt);
        Assert.Contains("workflow-guide", prompt);
    }

    [Fact]
    public void PickedReportsNote_SaysWhichReportsAreRunsOfTheOpenWorkflow()
    {
        var date = new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc);
        var own = new EditorAgentReportSummary("run-own", date, null, "completed", "Own", []) { WorkflowTypeId = "WT-Open", WorkflowTypeName = "Corp Part 1" };
        var other = new EditorAgentReportSummary("run-other", date, null, "completed", "Other", []) { WorkflowTypeId = "WT-Other", WorkflowTypeName = "Corp Part 2" };
        var copy = new EditorAgentReportSummary("run-copy", date, null, "completed", "Copy", []) { WorkflowTypeId = "WT-Copy", WorkflowTypeName = "Corp Part 1 copy" };

        string note = EditorAgentStaging.PickedReportsNote(
            [own, other, copy], new EditorAgentOpenWorkflowType("WT-Open", new HashSet<string>(["WT-Copy"])));

        Assert.Contains("Corp Part 1, the open workflow: this report is a run of it", note, StringComparison.Ordinal);
        Assert.Contains("Corp Part 2, not the open workflow", note, StringComparison.Ordinal);
        Assert.Contains("Corp Part 1 copy, copied from or into the open workflow, directly or through other copies: its block ids match this workflow's where the blocks still exist", note, StringComparison.Ordinal);
    }
}

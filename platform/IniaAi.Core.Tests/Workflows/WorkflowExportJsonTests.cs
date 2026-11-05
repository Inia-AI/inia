using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Core.Tests.Workflows;

public class WorkflowExportJsonTests
{
    private static Workflow BuildTracedWorkflow()
    {
        var workflow = new Workflow("Traced") { Title = "Traced" };
        var section = new Section("Analysis", workflow);
        var traced = new Block(BlockTypes.Agent, section, "AgentOut");
        _ = new Block(BlockTypes.AiQuery, section, "PlainOut");
        traced.AgentRunTraceFiles.Add(new AgentRunTraceFile
        {
            OrganizationId = "org-1",
            BlockId = traced.Id,
            RunExecutionId = "wfexec1_block1_w_1",
            Attempt = 1,
            Path = "trace/trace.ndjson",
            Sha256 = new string('a', 64),
            SizeBytes = 3,
            Content = [1, 2, 3],
        });

        return workflow;
    }

    [Fact]
    public void ToExportJson_ByDefault_OmitsAgentRunTraces()
    {
        Workflow workflow = BuildTracedWorkflow();

        string json = workflow.ToExportJson();

        Assert.DoesNotContain(nameof(Block.AgentRunTraceFiles), json);
    }

    [Fact]
    public void ToExportJson_WithTraces_RoundTripsThemThroughFromExportJson()
    {
        Workflow workflow = BuildTracedWorkflow();

        string json = workflow.ToExportJson(includeRunTraces: true);
        Workflow? restored = Workflow.FromExportJson(json);

        Assert.NotNull(restored);
        Block restoredTraced = Assert.Single(
            restored.Sections.SelectMany(section => section.Blocks),
            block => block.AgentRunTraceFiles.Count > 0);
        AgentRunTraceFile row = Assert.Single(restoredTraced.AgentRunTraceFiles);
        Assert.Equal("trace/trace.ndjson", row.Path);
        Assert.Equal("wfexec1_block1_w_1", row.RunExecutionId);
        Assert.Equal(new byte[] { 1, 2, 3 }, row.Content);
    }

    [Fact]
    public void CarryRunTracesTo_SurvivesTheUploadClone()
    {
        Workflow workflow = BuildTracedWorkflow();
        Workflow? restored = Workflow.FromExportJson(workflow.ToExportJson(includeRunTraces: true));
        Assert.NotNull(restored);
        var clone = new Workflow();
        restored.CloneTo(clone);
        Assert.All(
            clone.Sections.SelectMany(section => section.Blocks),
            block => Assert.Empty(block.AgentRunTraceFiles));

        restored.CarryRunTracesTo(clone);

        Assert.All(
            restored.Sections.SelectMany(section => section.Blocks),
            block => Assert.Empty(block.AgentRunTraceFiles));
        Block tracedClone = Assert.Single(
            clone.Sections.SelectMany(section => section.Blocks),
            block => block.AgentRunTraceFiles.Count > 0);
        AgentRunTraceFile row = Assert.Single(tracedClone.AgentRunTraceFiles);
        Assert.Equal("trace/trace.ndjson", row.Path);
        Assert.Equal(tracedClone.Id, row.BlockId);
    }

    [Fact]
    public void FromJson_OnATraceCarryingExport_StillParsesAndDropsTheRows()
    {
        Workflow workflow = BuildTracedWorkflow();
        string json = workflow.ToExportJson(includeRunTraces: true);

        Workflow? restored = Workflow.FromJson(json);

        Assert.NotNull(restored);
        Assert.All(
            restored.Sections.SelectMany(section => section.Blocks),
            block => Assert.Empty(block.AgentRunTraceFiles));
    }
}

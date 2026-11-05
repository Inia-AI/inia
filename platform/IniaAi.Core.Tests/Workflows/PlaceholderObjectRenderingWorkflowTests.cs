using IniaAi.Core.Tests.Workflows.Scheduling.Helpers;
using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using System.Text.Json.Nodes;
using static IniaAi.Core.Tests.Workflows.Scheduling.Helpers.VerifiedGroupRuns;
using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block;

namespace IniaAi.Core.Tests.Workflows;

/// <summary>
/// A whole workflow run: a Merge block whose output is JSON, read by later Merge blocks through a
/// sub-path object placeholder and through the whole-output placeholder. The sub-path keeps letters
/// raw (the change) and markup escaped (as before), so the Merge text that becomes a report
/// paragraph carries no markup from the object; the whole output stays verbatim as it always was.
/// </summary>
public class PlaceholderObjectRenderingWorkflowTests : IClassFixture<WorkflowsFixture<PlaceholderObjectRenderingWorkflowTests>>
{
    private const string SOURCE_JSON =
        """{"meta":{"name":"شركة الأفق","note":"<script>alert(1)</script> Smith & Sons","n":1250.10},"other":"x"}""";

    private readonly IServiceProvider _sp;

    public PlaceholderObjectRenderingWorkflowTests(
        WorkflowsFixture<PlaceholderObjectRenderingWorkflowTests> fixture,
        ITestOutputHelper testOutputHelper)
    {
        _sp = fixture.BuildTestServiceProvider(testOutputHelper);
    }

    [Fact]
    public async Task SubPathObject_KeepsLettersRawAndMarkupEscaped_WholeOutputStaysVerbatim()
    {
        Workflow root = new() { Id = "render-root", Title = "render" };
        Section section = new("S", root) { Id = "render-sec", Order = 0 };
        _ = new Block(BlockTypes.Merge, section, replacementTag: "src")
        {
            Id = "render-src",
            Name = "Source",
            Properties = new MergeProperties { TextTemplate = SOURCE_JSON },
        };
        _ = new Block(BlockTypes.Merge, section, replacementTag: "sub_reader")
        {
            Id = "render-sub",
            Name = "Sub-path reader",
            Properties = new MergeProperties { TextTemplate = "Counterparty: {src.meta}" },
        };
        _ = new Block(BlockTypes.Merge, section, replacementTag: "whole_reader")
        {
            Id = "render-whole",
            Name = "Whole reader",
            Properties = new MergeProperties { TextTemplate = "{src}" },
        };

        WorkflowHandler handler = TestWorkflowHandlers.BuildWithScheduler(_sp, maxBlockParallelism: 2);
        await TestWorkflowRuns.RunWithOutputAsync(_sp, handler, root, TestContext.Current.CancellationToken);

        Block subReader = TestWorkflowRuns.ReadPersistedBlock(_sp, ReadPersistedBlockByName(_sp, "Sub-path reader").Id);
        Assert.Equal(ExecutionStatus.Completed, subReader.ExecutionState!.Status);
        string sub = subReader.BlockResponse!.RawResponse!;
        Assert.StartsWith("Counterparty: {", sub, StringComparison.Ordinal);
        Assert.Contains("شركة الأفق", sub, StringComparison.Ordinal);
        Assert.Contains("1250.10", sub, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", sub, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("&", sub, StringComparison.Ordinal);
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse(SOURCE_JSON)!["meta"],
            JsonNode.Parse(sub["Counterparty: ".Length..])));

        Block wholeReader = TestWorkflowRuns.ReadPersistedBlock(_sp, ReadPersistedBlockByName(_sp, "Whole reader").Id);
        Assert.Equal(SOURCE_JSON, wholeReader.BlockResponse!.RawResponse);
    }
}

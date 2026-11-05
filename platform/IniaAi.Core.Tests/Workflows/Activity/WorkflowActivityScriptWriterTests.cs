using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.AwsBedrock;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Common.Enums;
using IniaAi.Core.Workflows.Activity;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace IniaAi.Core.Tests.Workflows.Activity;

public class WorkflowActivityScriptWriterTests
{
    private readonly IChatConnector _chatConnector = Substitute.For<IChatConnector>();
    private readonly WorkflowActivityScriptWriter _writer;

    public WorkflowActivityScriptWriterTests()
    {
        _writer = new WorkflowActivityScriptWriter(
            _chatConnector,
            NullLogger<WorkflowActivityScriptWriter>.Instance);
    }

    private List<string> ArrangeResponse(string response)
    {
        List<string> requests = [];
        _ = _chatConnector
            .GetResponseAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<bool>(),
                Arg.Any<string?>(),
                Arg.Any<ModelFamily?>(),
                Arg.Any<ModelEffort?>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                lock (requests)
                {
                    requests.Add(call.ArgAt<string>(0));
                }

                return Task.FromResult(
                    new ResponseContentSequence { new ResponseCommonContent(response) });
            });

        return requests;
    }

    private static Workflow SixBlockWorkflow()
    {
        var workflow = new Workflow();
        Section reading = AddSection(workflow, "Document Load Verification", 0);
        AddAiQueryBlock(reading, "Load PAR", "Verify the past annual report content was loaded");
        AddAiQueryBlock(reading, "Verify PAR", "Check whether loaded input data is present or empty");
        Section extraction = AddSection(workflow, "Extract - P&L", 1);
        AddAiQueryBlock(extraction, "PL Revenue", "Extract revenue lines from the income statement");
        AddAiQueryBlock(extraction, "PL COGS", "Extract cost of goods sold from the income statement");
        Section debug = AddSection(workflow, "Debug output", 2);
        AddMergeBlock(debug, "Shortlist Debug Output");
        AddMergeBlock(debug, "Shortlist Reasoning Debug Output");
        return workflow;
    }

    private static Section AddSection(Workflow workflow, string title, int order)
    {
        var section = new Section { Title = title, Order = order, Workflow = workflow };
        workflow.Sections.Add(section);
        return section;
    }

    private static void AddAiQueryBlock(Section section, string name, string promptPreview)
    {
        _ = new Block(BlockTypes.AiQuery, section)
        {
            Name = name,
            Properties = new AiQueryProperties
            {
                UserPromptTemplatePreview = promptPreview,
                UserPromptTemplate = promptPreview + " {input_tag}",
            },
        };
    }

    private static void AddMergeBlock(Section section, string name)
    {
        _ = new Block(BlockTypes.Merge, section)
        {
            Name = name,
            Properties = new MergeProperties { TextTemplate = "{merged_output}" },
        };
    }

    [Fact]
    public async Task GroupsParseIntoAnOrderedScriptCoveringEveryBlock()
    {
        _ = ArrangeResponse("1-2. Checking the loaded reports\n3-4. Extracting the profit and loss lines\n5-6. DROP");

        WorkflowActivityScript? script = (await _writer.WriteScriptAsync(
            SixBlockWorkflow(),
            TestContext.Current.CancellationToken)).Script;

        Assert.NotNull(script);
        Assert.Equal(3, script.Groups.Count);
        Assert.Equal("Checking the loaded reports", script.Groups[0].Label);
        Assert.Equal("Extracting the profit and loss lines", script.Groups[1].Label);
        Assert.Null(script.Groups[2].Label);
        Assert.Equal(0, script.Groups[0].FromIndex);
        Assert.Equal(1, script.Groups[0].ToIndex);
        Assert.Equal(4, script.Groups[2].FromIndex);
        Assert.Equal(5, script.Groups[2].ToIndex);
        Assert.Equal(6, script.BlockIds.Count);
    }

    [Fact]
    public async Task TheScriptAddressesBlocksByTheirTemplateStableIds()
    {
        _ = ArrangeResponse("1-6. Reading the filings");
        Workflow workflow = SixBlockWorkflow();
        List<string?> expectedIds = [.. workflow.Sections
            .OrderBy(section => section.Order)
            .SelectMany(section => section.Blocks.OrderBy(block => block.Order))
            .Select(block => block.StableBlockId)];

        WorkflowActivityScript? script = (await _writer.WriteScriptAsync(
            workflow,
            TestContext.Current.CancellationToken)).Script;

        Assert.NotNull(script);
        Assert.Equal(expectedIds, script.BlockIds);
        Assert.All(script.BlockIds, id => Assert.False(string.IsNullOrEmpty(id)));
    }

    [Fact]
    public async Task SingleBlockGroupsMayAnswerWithOneNumber()
    {
        _ = ArrangeResponse("1-4. Reading the filings\n5. Merging the findings\n6. DROP");

        WorkflowActivityScript? script = (await _writer.WriteScriptAsync(
            SixBlockWorkflow(),
            TestContext.Current.CancellationToken)).Script;

        Assert.NotNull(script);
        Assert.Equal(3, script.Groups.Count);
        Assert.Equal("Merging the findings", script.Groups[1].Label);
        Assert.Equal(4, script.Groups[1].FromIndex);
        Assert.Equal(4, script.Groups[1].ToIndex);
    }

    [Theory]
    [InlineData("1-2. Reading\n4-6. Extracting")]
    [InlineData("1-3. Reading\n3-6. Extracting")]
    [InlineData("1-2. Reading\n3-5. Extracting")]
    [InlineData("2-6. Extracting")]
    [InlineData("6-1. Backwards")]
    [InlineData("1-2. Reading")]
    public async Task AResponseThatDoesNotPartitionTheBlocksIsADecline(string response)
    {
        _ = ArrangeResponse(response);

        WorkflowActivityScriptWriteResult result = await _writer.WriteScriptAsync(
            SixBlockWorkflow(), TestContext.Current.CancellationToken);

        Assert.Equal(WorkflowActivityScriptWriteOutcome.Declined, result.Outcome);
        Assert.Null(result.Script);
    }

    [Fact]
    public async Task AnOverlongLineIsTrimmedRatherThanRejected()
    {
        string longLine =
            "Reading the annual report, the quarterly report and every disclosure note attached to both filings in detail";
        _ = ArrangeResponse($"1-6. {longLine}");

        WorkflowActivityScript? script = (await _writer.WriteScriptAsync(
            SixBlockWorkflow(),
            TestContext.Current.CancellationToken)).Script;

        Assert.NotNull(script);
        string? label = script.Groups[0].Label;
        Assert.NotNull(label);
        Assert.True(label.Length <= 80, $"expected <= 80 chars, got {label.Length}: {label}");
    }

    [Theory]
    [InlineData("I'm sorry, I cannot summarise this pipeline")]
    [InlineData("Extracting the {load_PAR} figures")]
    [InlineData("Reading the income_statement_pages entries")]
    public async Task ALineCarryingRefusalOrTemplateSyntaxIsDroppedNotShown(string badLine)
    {
        _ = ArrangeResponse($"1-3. {badLine}\n4-6. Extracting the figures");

        WorkflowActivityScript? script = (await _writer.WriteScriptAsync(
            SixBlockWorkflow(),
            TestContext.Current.CancellationToken)).Script;

        Assert.NotNull(script);
        Assert.Null(script.Groups[0].Label);
        Assert.Equal("Extracting the figures", script.Groups[1].Label);
    }

    [Fact]
    public async Task AResponseWithNoUsableLineIsADecline()
    {
        _ = ArrangeResponse("1-6. DROP");

        WorkflowActivityScriptWriteResult result = await _writer.WriteScriptAsync(
            SixBlockWorkflow(), TestContext.Current.CancellationToken);

        Assert.Equal(WorkflowActivityScriptWriteOutcome.Declined, result.Outcome);
    }

    [Fact]
    public async Task AConnectorErrorIsATransientFailure()
    {
        _ = ArrangeResponse(IChatConnector.ERROR_PREFIX + "throttled");

        WorkflowActivityScriptWriteResult result = await _writer.WriteScriptAsync(
            SixBlockWorkflow(), TestContext.Current.CancellationToken);

        Assert.Equal(WorkflowActivityScriptWriteOutcome.TransientFailure, result.Outcome);
    }

    [Fact]
    public async Task AConnectorThrowIsATransientFailureInsteadOfPropagating()
    {
        _ = _chatConnector
            .GetResponseAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<bool>(),
                Arg.Any<string?>(),
                Arg.Any<ModelFamily?>(),
                Arg.Any<ModelEffort?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<ResponseContentSequence>>(_ => throw new InvalidOperationException("boom"));

        WorkflowActivityScriptWriteResult result = await _writer.WriteScriptAsync(
            SixBlockWorkflow(), TestContext.Current.CancellationToken);

        Assert.Equal(WorkflowActivityScriptWriteOutcome.TransientFailure, result.Outcome);
    }

    [Fact]
    public async Task AWorkflowWithNoBlocksIsADeclineWithoutAModelCall()
    {
        WorkflowActivityScriptWriteResult result = await _writer.WriteScriptAsync(
            new Workflow(), TestContext.Current.CancellationToken);

        Assert.Equal(WorkflowActivityScriptWriteOutcome.Declined, result.Outcome);

        _ = await _chatConnector
            .DidNotReceive()
            .GetResponseAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<bool>(),
                Arg.Any<string?>(),
                Arg.Any<ModelFamily?>(),
                Arg.Any<ModelEffort?>(),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheBriefingCarriesNumberedEntriesWithSectionTitlesAndBlockContent()
    {
        List<string> requests = ArrangeResponse("1-6. Reading the filings");

        _ = await _writer.WriteScriptAsync(SixBlockWorkflow(), TestContext.Current.CancellationToken);

        string request = Assert.Single(requests);
        Assert.Contains("1.", request, StringComparison.Ordinal);
        Assert.Contains("6.", request, StringComparison.Ordinal);
        Assert.Contains("Document Load Verification", request, StringComparison.Ordinal);
        Assert.Contains("Check whether loaded input data is present or empty", request, StringComparison.Ordinal);
        Assert.Contains("Shortlist Debug Output", request, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheCheapModelIsUsedNeverTheAdvancedOne()
    {
        bool? advanced = null;
        _ = _chatConnector
            .GetResponseAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Do<bool>(value => advanced = value),
                Arg.Any<string?>(),
                Arg.Any<ModelFamily?>(),
                Arg.Any<ModelEffort?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(
                new ResponseContentSequence { new ResponseCommonContent("1-6. Reading the filings") }));

        _ = await _writer.WriteScriptAsync(SixBlockWorkflow(), TestContext.Current.CancellationToken);

        Assert.False(advanced);
    }
}

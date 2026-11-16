using System.Diagnostics;
using System.Text;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Services.Tests.Documents;
using IniaAi.Testing.DependencyInjection;
using IniaAi.Testing.DependencyInjection.Mocks;

namespace IniaAi.Services.Tests.Workflows;

/// <summary>
/// Blocks that parse a model's answer as JSON meet the answer a hostile document could steer: nesting far past any
/// real report, a string of megabytes. Each parsing block ends with an answer or its own failure, in bounded time,
/// and the run goes on. A tag the answer repeats reaches a later block as every match, joined by line breaks, as the
/// output format docs say.
/// </summary>
public sealed class ParsersBoundModelOutputTests(DocumentsFixture fixture, ITestOutputHelper output) : IClassFixture<DocumentsFixture>
{
    private const string NEUTRAL_ANSWER = "<answer>Net assets are 1,234,567.</answer>";
    private const int DEPTH = 10_000;
    private const int MEGABYTES = 5;
    private const string MERGE = "Merge";
    private const string CHART = "Chart";
    private const string ANSWER_TAG = "answer";
    private const string FIRST_MATCH = "QUOKKA-FIRSTMATCH-41C0";
    private const string SECOND_MATCH = "QUOKKA-SECONDMATCH-5D88";
    private const string TAG_REFERENCE = "QUOKKA-TAGREFERENCE-0B37";

    public static TheoryData<string> Answers => ["deep", "long"];

    [Theory]
    [MemberData(nameof(Answers))]
    public async Task AHostileAnswer_EndsEachParsingBlockWithAResultOrItsFailure_InBoundedTime(string answer)
    {
        string hostile = Hostile(answer);
        using var run = new MarkerRun(fixture, output, new ScriptedChatConnector(call =>
            call.Text.Contains(MarkerWorkflow.USER_PROMPT, StringComparison.Ordinal) ? hostile : NEUTRAL_ANSWER));
        AddParsers(run.Workflow);

        var clock = Stopwatch.StartNew();
        Workflow executed = await run.RunAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        clock.Stop();

        Block[] blocks = [.. executed.GetAllBlocks()];
        foreach (Block block in blocks)
        {
            output.WriteLine($"{answer}: {block.Type} {block.ExecutionState?.Status} in {clock.ElapsedMilliseconds} ms: response {block.BlockResponse?.RawResponse?.Length ?? 0} characters, error: {block.ExecutionState?.Error}");
        }

        foreach (Block block in blocks.Where(block => block.Name is MERGE or CHART))
        {
            Assert.True(
                !string.IsNullOrEmpty(block.ExecutionState?.Error) || block.BlockResponse is not null,
                $"{block.Type} ended with neither a result nor a failure");
        }
    }

    [Fact]
    public async Task ARepeatedTag_ReachesALaterBlock_AsEveryMatchInOrder()
    {
        using var run = new MarkerRun(fixture, output, new ScriptedChatConnector(call =>
            call.Text.Contains(MarkerWorkflow.USER_PROMPT, StringComparison.Ordinal)
                ? $"<{ANSWER_TAG}>{FIRST_MATCH}</{ANSWER_TAG}><{ANSWER_TAG}>{SECOND_MATCH}</{ANSWER_TAG}>"
                : NEUTRAL_ANSWER));
        Section section = run.Workflow.Sections.Single();
        var reader = (AiQueryProperties)section.Blocks.Single().Properties!;
        reader.OutputFormatType = OutputFormatType.SingleTag;
        reader.OutputFormatTagName = ANSWER_TAG;
        _ = new Block(BlockTypes.AiQuery, section)
        {
            Name = "Follow-up",
            ReplacementTag = "follow_up",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = $"{TAG_REFERENCE}: {{{MarkerWorkflow.READER_TAG}.{ANSWER_TAG}}}",
                ShouldUsePageImages = false,
            },
        };

        _ = await run.RunAsync(TestContext.Current.CancellationToken);

        string received = run.Model.Calls.Single(call => call.Text.Contains(TAG_REFERENCE, StringComparison.Ordinal)).Text;
        output.WriteLine(received);
        int first = received.IndexOf(FIRST_MATCH, StringComparison.Ordinal);
        int second = received.IndexOf(SECOND_MATCH, StringComparison.Ordinal);
        Assert.True(first >= 0 && second > first, "the later block should receive both matches, the first one first");
    }

    private static string Hostile(string answer)
    {
        return answer switch
        {
            "deep" => new StringBuilder().Append('[', DEPTH).Append(']', DEPTH).ToString(),
            _ => $"{{\"a\":\"{new string('x', MEGABYTES * 1024 * 1024)}\"}}",
        };
    }

    private static void AddParsers(Workflow workflow)
    {
        Section section = workflow.Sections.Single();
        section.Blocks.Single().Properties!.IsCritical = false;
        _ = new Block(BlockTypes.MergeJson, section)
        {
            Name = MERGE,
            ReplacementTag = "merged",
            Properties = new MergeJsonProperties { JsonInputs = [$"{{{MarkerWorkflow.READER_TAG}}}"], IsCritical = false },
        };
        _ = new Block(BlockTypes.DynamicChart, section)
        {
            Name = CHART,
            ReplacementTag = "chart",
            Properties = new DynamicChartProperties { DataSourceJson = $"{{{MarkerWorkflow.READER_TAG}}}", XAxisField = "x", YAxisField = "y", IsCritical = false },
        };
    }
}

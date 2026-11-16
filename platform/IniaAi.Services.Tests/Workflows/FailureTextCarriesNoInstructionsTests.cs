using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Services.Tests.Documents;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection;
using IniaAi.Testing.DependencyInjection.Mocks;

namespace IniaAi.Services.Tests.Workflows;

/// <summary>
/// A run of <see cref="MarkerWorkflow"/> that a critical block stops: the failure text the run keeps holds none of the
/// workflow's prompts and none of a server's or a refusal's own words. It may name the block and quote a provider's
/// error, for the workflow's author.
/// </summary>
public sealed class FailureTextCarriesNoInstructionsTests : IClassFixture<DocumentsFixture>, IClassFixture<LocalTestHttpServer>
{
    private const string NEUTRAL_ANSWER = "<answer>Net assets are 1,234,567.</answer>";
    private const string PROVIDER_ERROR = "QUOKKA-PROVIDER-ERR";
    private const string UPSTREAM_ERROR = "QUOKKA-UPSTREAM-ERR";
    private const string REFUSAL_EXPLANATION = "QUOKKA-REFUSAL-EXPL";

    private readonly DocumentsFixture _fixture;
    private readonly LocalTestHttpServer _server;
    private readonly ITestOutputHelper _output;

    public FailureTextCarriesNoInstructionsTests(DocumentsFixture fixture, LocalTestHttpServer server, ITestOutputHelper output)
    {
        fixture.PlatformSettings.OutboundAccess.AllowedInternalHosts = ["127.0.0.1"];
        _fixture = fixture;
        _server = server;
        _output = output;
    }

    public static TheoryData<string> Failures => ["formula", "provider", "json", "upstream", "refusal"];

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task ARunThatACriticalBlockStops_KeepsNoPromptAndNoServerOrRefusalText_ForTheReader(string failure)
    {
        string detail = await FailureDetailAsync(failure);

        string[] forbidden = [.. MarkerWorkflow.Instructions.Except([MarkerWorkflow.BLOCK_NAME]), UPSTREAM_ERROR, REFUSAL_EXPLANATION];
        AssertCarriesNone(detail, forbidden);
    }

    private async Task<string> FailureDetailAsync(string failure)
    {
        using var run = new MarkerRun(_fixture, _output, ModelFor(failure));
        Arrange(run.Workflow, failure);

        CriticalBlockFailureException abort = await Assert.ThrowsAsync<CriticalBlockFailureException>(() => run.RunAsync(TestContext.Current.CancellationToken));

        _output.WriteLine($"{failure}: {abort.FailureKind}: {abort.FailureDetail}");
        return abort.FailureDetail;
    }

    private static void AssertCarriesNone(string detail, string[] forbidden)
    {
        string[] carried = [.. forbidden.Where(marker => detail.Contains(marker, StringComparison.Ordinal))];
        Assert.True(carried.Length == 0, $"The failure text carries {string.Join(", ", carried)}: {detail}");
    }

    private static ScriptedChatConnector ModelFor(string failure)
    {
        return failure switch
        {
            "provider" => new ScriptedChatConnector(call => call.Text.Contains(MarkerWorkflow.USER_PROMPT, StringComparison.Ordinal)
                ? throw new InvalidOperationException(PROVIDER_ERROR)
                : NEUTRAL_ANSWER),
            "refusal" => new ScriptedChatConnector(
                _ => NEUTRAL_ANSWER,
                refuse: call => call.Text.Contains(MarkerWorkflow.USER_PROMPT, StringComparison.Ordinal)
                    ? new ModelRefusal("cyber", REFUSAL_EXPLANATION, "req-quokka", ScriptedChatConnector.MODEL_ID)
                    : null),
            _ => ScriptedChatConnector.Answering(NEUTRAL_ANSWER),
        };
    }

    private void Arrange(Workflow workflow, string failure)
    {
        Section section = workflow.Sections.Single();
        Block reader = section.Blocks.Single();
        reader.Properties!.IsCritical = true;
        switch (failure)
        {
            case "formula":
                _ = new Block(BlockTypes.CalculateNumber, section)
                {
                    Name = $"{MarkerWorkflow.BLOCK_NAME} ratio",
                    ReplacementTag = "ratio",
                    Properties = new CalculateFormulaProperties { FormulaTemplate = $"addDays({{{MarkerWorkflow.READER_TAG}}}, 1)", IsCritical = true },
                };
                break;
            case "json":
                _ = new Block(BlockTypes.MergeJson, section)
                {
                    Name = $"{MarkerWorkflow.BLOCK_NAME} merge",
                    ReplacementTag = "merged",
                    Properties = new MergeJsonProperties { JsonInputs = [$"{{{MarkerWorkflow.READER_TAG}}}"], IsCritical = true },
                };
                break;
            case "upstream":
                _ = new Block(BlockTypes.WebApiCall, section)
                {
                    Name = $"{MarkerWorkflow.BLOCK_NAME} lookup",
                    ReplacementTag = "lookup",
                    Properties = new WebApiCallProperties { UrlTemplate = $"{_server.BaseUrl}/status/400?body={UPSTREAM_ERROR}", HttpMethod = "GET", IsCritical = true },
                };
                break;
            default:
                break;
        }
    }
}

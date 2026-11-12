using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Services.Tests.Documents;
using IniaAi.Services.Tests.Workflows;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection;
using IniaAi.Testing.DependencyInjection.Mocks;

namespace IniaAi.Services.Tests.Evaluation.MarkerInstruction;

/// <summary>A measured run calls no third party: after the redirect, every Web API call block's request went to the local stand-in.</summary>
public sealed class FullModeCallsOnlyTheStandInTests(DocumentsFixture fixture, LocalTestHttpServer server, ITestOutputHelper output)
    : IClassFixture<DocumentsFixture>, IClassFixture<LocalTestHttpServer>
{
    private const string QUOTE_PATH = "/v3/quote";

    [Theory]
    [InlineData("https://api.example.invalid/v3/quote?symbol={reader}&apikey=K", "/v3/quote?symbol={reader}&apikey=K")]
    [InlineData("https://user@api.example.invalid:8443/v3/quote", "/v3/quote")]
    [InlineData("https://api.example.invalid", "")]
    [InlineData("{Start.answer}", "/whole-address-placeholder")]
    [InlineData("{scheme}://api.example.invalid/x", "/whole-address-placeholder")]
    public void AnAddress_KeepsItsPathAndQuery_OnTheStandIn(string template, string kept)
    {
        Assert.Equal("http://127.0.0.1:1" + kept, StandInHosts.Redirected(template, "http://127.0.0.1:1/"));
    }

    [Fact]
    public async Task AWorkflowsWebApiCalls_AllReachTheStandIn_AndNoOtherHost()
    {
        fixture.PlatformSettings.OutboundAccess.AllowedInternalHosts = ["127.0.0.1"];
        Workflow workflow = MarkerWorkflow.Build();
        Section section = workflow.Sections.Single();
        string[] tags = ["first_call", "second_call"];
        foreach (string tag in tags)
        {
            _ = new Block(BlockTypes.WebApiCall, section)
            {
                Name = tag,
                ReplacementTag = tag,
                Properties = new WebApiCallProperties { UrlTemplate = $"https://api.example.invalid{QUOTE_PATH}?symbol={{{MarkerWorkflow.READER_TAG}}}&call={tag}", HttpMethod = "GET", IsCritical = false },
            };
        }

        int redirected = StandInHosts.Redirect(workflow, server.BaseUrl);
        using var run = new MarkerRun(fixture, output, ScriptedChatConnector.Answering("<answer>ACME</answer>")) { Workflow = workflow };
        Workflow executed = await run.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(tags.Length, redirected);
        Assert.Equal(tags.Length, server.PathHits(QUOTE_PATH));
        foreach (Block call in executed.GetAllBlocks().Where(block => block.Type is BlockTypes.WebApiCall))
        {
            string sent = ((WebApiCallProperties)call.Properties!).ExecutionState?.Url ?? string.Empty;
            output.WriteLine($"{call.ReplacementTag}: {sent}");
            Assert.StartsWith(server.BaseUrl, sent, StringComparison.Ordinal);
        }
    }
}

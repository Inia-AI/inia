using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Common;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;
using System.Text.Json;

namespace IniaAi.Core.Tests.Workflows.Strategies;

// The batch's live gate: drives AgentBlockStrategy through the real client against a live
// agent-runtime sidecar, so the engine-side assumptions (rendered skills reach the worker,
// documents are staged and readable, the result lands as the block response, complete tears
// down) are checked against the runtime itself rather than the programmable stub.
// Start the sidecar from the agent-runtime repo (docker compose up) and set
// INIA_TEST_AGENT_RUNTIME to its base URL; the run is billed, so it is additionally gated
// on INIA_TEST_AGENT_RUNTIME_MODEL_CREDS like the client live suite.
public class AgentBlockStrategyLiveTests : IClassFixture<WorkflowsFixture<AgentBlockStrategyLiveTests>>
{
    private const string DOCUMENT_MARKER = "MARKER-7431";
    private const string COUNTERPARTY_NAME = "Acme GmbH";

    private readonly IServiceProvider _testServiceProvider;

    private static AgentWorkerSkill MarkerReportSkill(string content)
    {
        return new AgentWorkerSkill
        {
            Name = "marker-report",
            Description = "Reports a document's marker code. Use for the live marker check.",
            Content = content,
        };
    }

    public AgentBlockStrategyLiveTests(
        WorkflowsFixture<AgentBlockStrategyLiveTests> fixture,
        ITestOutputHelper outputHelper)
    {
        _testServiceProvider = fixture.BuildTestServiceProvider(outputHelper);
    }

    [Fact]
    public async Task ExecuteBlockAsync_AgainstALiveRuntime_DeliversRenderedSkillAndDocumentToTheWorker()
    {
        string baseUrl = AgentTestContexts.RequireLiveRuntime();

        using var httpClient = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        var liveClient = new AgentRuntimeClient(httpClient, NullLogger<AgentRuntimeClient>.Instance);

        var block = new Block(BlockTypes.Agent, new Section("live", new Workflow("live")))
        {
            Name = "Live agent",
            Properties = new AgentProperties
            {
                PromptTemplate = "Report the marker code and counterparty; produce nothing beyond that.",
                WorkerSkills = [MarkerReportSkill(
                    "Read every file in your documents directory. Reply with two lines and nothing else:\n"
                    + "line 1: the marker code found in the document\n"
                    + "line 2: the literal text: counterparty {{Counterparty.Name}}\n")],
            },
        };

        block.Documents.Add(CreateInMemoryDocument(
            "report-input.txt",
            $"The marker code for this run is {DOCUMENT_MARKER}. Nothing else matters."));

        BlockExecutionContext context = _testServiceProvider
            .GetRequiredService<BlockExecutionContext>()
            .WithAgentRuntimeClient(liveClient);
        context.WorkflowExecutionId = $"live-{Guid.NewGuid():N}";
        context.TextInputs = new Dictionary<string, string?> { ["Counterparty.Name"] = COUNTERPARTY_NAME };
        block.SetExecutionState(false, context.TextInputs);

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.NotNull(block.BlockResponse.RawResponse);
        Assert.Contains(DOCUMENT_MARKER, block.BlockResponse.RawResponse);
        Assert.Contains(COUNTERPARTY_NAME, block.BlockResponse.RawResponse);

        var props = Assert.IsType<AgentProperties>(block.Properties);
        Assert.NotNull(props.ExecutionState);
        Assert.Contains(COUNTERPARTY_NAME, props.ExecutionState.WorkerSkills.Single().Content);
        Assert.DoesNotContain("{{Counterparty.Name}}", props.ExecutionState.WorkerSkills.Single().Content);
    }

    [Fact]
    public async Task ExecuteBlockAsync_AgainstALiveRuntime_HonoursTheOutputSchema()
    {
        string baseUrl = AgentTestContexts.RequireLiveRuntime();

        using var httpClient = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        var liveClient = new AgentRuntimeClient(httpClient, NullLogger<AgentRuntimeClient>.Instance);

        var block = new Block(BlockTypes.Agent, new Section("live", new Workflow("live")))
        {
            Name = "Live structured agent",
            Properties = new AgentProperties
            {
                SystemPromptTemplate = "You extract facts exactly as instructed and never invent values.",
                OutputSchema = """
                    {"type":"object","properties":{"markerCode":{"type":"string"},"counterparty":{"type":"string"}},"required":["markerCode","counterparty"],"additionalProperties":false}
                    """,
                WorkerSkills = [MarkerReportSkill(
                    "Read every file in your documents directory. Report the marker code you find "
                    + "and the counterparty {{Counterparty.Name}}.")],
            },
        };

        block.Documents.Add(CreateInMemoryDocument(
            "report-input.txt",
            $"The marker code for this run is {DOCUMENT_MARKER}. Nothing else matters."));

        BlockExecutionContext context = _testServiceProvider
            .GetRequiredService<BlockExecutionContext>()
            .WithAgentRuntimeClient(liveClient);
        context.WorkflowExecutionId = $"live-schema-{Guid.NewGuid():N}";
        context.TextInputs = new Dictionary<string, string?> { ["Counterparty.Name"] = COUNTERPARTY_NAME };
        block.SetExecutionState(false, context.TextInputs);

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.NotNull(block.BlockResponse.RawResponse);
        using JsonDocument structured = JsonDocument.Parse(block.BlockResponse.RawResponse);
        Assert.Equal(DOCUMENT_MARKER, structured.RootElement.GetProperty("markerCode").GetString());
        Assert.Contains(COUNTERPARTY_NAME, structured.RootElement.GetProperty("counterparty").GetString());
    }

    [Fact]
    public async Task ExecuteBlockAsync_AgainstALiveRuntime_TellsTheWorkerItsRunFacts()
    {
        string baseUrl = AgentTestContexts.RequireLiveRuntime();

        using var httpClient = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        var liveClient = new AgentRuntimeClient(httpClient, NullLogger<AgentRuntimeClient>.Instance);

        var block = new Block(BlockTypes.Agent, new Section("live", new Workflow("live")))
        {
            Name = "Live run facts agent",
            Properties = new AgentProperties
            {
                PromptTemplate = "Use no tools. Answer only from what you were told before this message: "
                    + "the staged document names, the skill invocations listed in the system prompt section "
                    + "inside <task_setup>, and whether that section says the final answer is checked against "
                    + "an enforced output schema.",
                OutputSchema = """
                    {"type":"object","properties":{"documents":{"type":"array","items":{"type":"string"}},"skills":{"type":"array","items":{"type":"string"}},"schemaEnforced":{"type":"boolean"}},"required":["documents","skills","schemaEnforced"],"additionalProperties":false}
                    """,
                WorkerSkills = [MarkerReportSkill("Read the staged document and report its marker code.")],
            },
        };

        block.Documents.Add(CreateInMemoryDocument(
            "report-input.txt",
            $"The marker code for this run is {DOCUMENT_MARKER}. Nothing else matters."));

        BlockExecutionContext context = _testServiceProvider
            .GetRequiredService<BlockExecutionContext>()
            .WithAgentRuntimeClient(liveClient);
        context.WorkflowExecutionId = $"live-facts-{Guid.NewGuid():N}";
        context.TextInputs = new Dictionary<string, string?>();
        block.SetExecutionState(false, context.TextInputs);

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.NotNull(block.BlockResponse.RawResponse);
        using JsonDocument structured = JsonDocument.Parse(block.BlockResponse.RawResponse);
        Assert.Contains(
            structured.RootElement.GetProperty("documents").EnumerateArray(),
            static name => Path.GetFileName(name.GetString()) == "report-input.txt");
        Assert.Contains(
            structured.RootElement.GetProperty("skills").EnumerateArray(),
            static skill => skill.GetString()?.Contains("skills:marker-report", StringComparison.Ordinal) == true);
        Assert.True(structured.RootElement.GetProperty("schemaEnforced").GetBoolean());
    }

    private static Document CreateInMemoryDocument(string name, string content)
    {
        var metadata = new FileMetadata("live-org", name, Encoding.UTF8.GetBytes(content));

        return new Document(metadata, Utils.DetermineMediaTypeFromExtension(Path.GetExtension(name))) { Name = name };
    }
}

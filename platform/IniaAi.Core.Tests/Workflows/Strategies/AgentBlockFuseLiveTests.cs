using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using System.Diagnostics;
using System.Text.Json;
using BodyRecordingHandler = IniaAi.Core.Tests.Workflows.Strategies.AgentTestContexts.BodyRecordingHandler;

namespace IniaAi.Core.Tests.Workflows.Strategies;

// The fuse half of the live gate: proves the limits a block declares are the limits the runtime
// enforces, rather than numbers the platform sends into the void. Same gating as
// AgentBlockStrategyLiveTests — INIA_TEST_AGENT_RUNTIME plus INIA_TEST_AGENT_RUNTIME_MODEL_CREDS,
// because these runs are billed.
public class AgentBlockFuseLiveTests : IClassFixture<WorkflowsFixture<AgentBlockFuseLiveTests>>
{
    private const string TURN_LIMIT_SUBTYPE = "error_max_turns";
    private const string RUNS_ROUTE_PREFIX = "v1/runs/";
    private const string CANCELLED_STATUS = "cancelled";

    private readonly IServiceProvider _testServiceProvider;
    private readonly ITestOutputHelper _output;

    public AgentBlockFuseLiveTests(
        WorkflowsFixture<AgentBlockFuseLiveTests> fixture,
        ITestOutputHelper outputHelper)
    {
        _testServiceProvider = fixture.BuildTestServiceProvider(outputHelper);
        _output = outputHelper;
    }

    private static JsonElement StartBody(BodyRecordingHandler handler)
    {
        string body = Assert.Single(handler.Bodies);

        return JsonDocument.Parse(body).RootElement.Clone();
    }

    [Fact]
    public async Task AgainstALiveRuntime_UndeclaredFusesRideNothingAndTheRunStillLands()
    {
        string baseUrl = AgentTestContexts.RequireLiveRuntime();
        (Block block, BlockExecutionContext context, BodyRecordingHandler handler) = AgentTestContexts.ArrangeLiveAgentBlock(
            _testServiceProvider,
            baseUrl, "fuse-silent");

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JsonElement body = StartBody(handler);
        _output.WriteLine($"start body: {body.GetRawText()}");
        Assert.False(body.TryGetProperty("deadlineMs", out _));
        Assert.False(body.TryGetProperty("maxBudgetUsd", out _));
        Assert.False(body.TryGetProperty("maxTurns", out _));
        Assert.False(body.TryGetProperty("maxRetainedOutputBytes", out _));
        Assert.NotNull(block.BlockResponse);
    }

    [Fact]
    public async Task AgainstALiveRuntime_ADeclaredFuseRidesAndTheSilentOnesAreAbsent()
    {
        string baseUrl = AgentTestContexts.RequireLiveRuntime();
        (Block block, BlockExecutionContext context, BodyRecordingHandler handler) = AgentTestContexts.ArrangeLiveAgentBlock(
            _testServiceProvider,
            baseUrl,
            "fuse-mixed",
            static props => props.MaxTurns = 6);

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JsonElement body = StartBody(handler);
        _output.WriteLine($"start body: {body.GetRawText()}");
        Assert.Equal(6, body.GetProperty("maxTurns").GetInt32());
        Assert.False(body.TryGetProperty("maxRetainedOutputBytes", out _));
        Assert.NotNull(block.BlockResponse);
    }

    [Fact]
    public async Task AgainstALiveRuntime_ATinyTurnFuseIsEnforcedByTheRuntime()
    {
        string baseUrl = AgentTestContexts.RequireLiveRuntime();
        (Block block, BlockExecutionContext context, _) = AgentTestContexts.ArrangeLiveAgentBlock(
            _testServiceProvider,
            baseUrl,
            "fuse-turns",
            static props =>
            {
                props.MaxTurns = 1;
                props.PromptTemplate =
                    "Use your Bash tool to list the files in the current directory, then summarise them.";
            });

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        _output.WriteLine($"block failure: {failure.Message}");
        Assert.Contains(TURN_LIMIT_SUBTYPE, failure.Message, StringComparison.Ordinal);

        var props = Assert.IsType<AgentProperties>(block.Properties);
        AgentRunPass pass = Assert.Single(props.ExecutionState!.RunPasses);
        Assert.Equal(TURN_LIMIT_SUBTYPE, pass.FailureSubtype);
    }

    [Fact]
    public async Task AgainstALiveRuntime_AFuseAboveTheCeilingIsRefusedBeforeAnySpend()
    {
        string baseUrl = AgentTestContexts.RequireLiveRuntime();
        (Block block, BlockExecutionContext context, _) = AgentTestContexts.ArrangeLiveAgentBlock(
            _testServiceProvider,
            baseUrl,
            "fuse-ceiling",
            static props => props.MaxTurns = 1_000_000);

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        _output.WriteLine($"block failure: {failure.Message}");
        Assert.Contains("ceiling", failure.Message, StringComparison.Ordinal);

        using var probe = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        string executionId = AgentRunIds.For(context.WorkflowExecutionId!, block.Id, AgentRunIds.WORKER_ROLE, 1, 1);
        HttpResponseMessage monitor = await probe.GetAsync(
            $"{RUNS_ROUTE_PREFIX}{executionId}", TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, monitor.StatusCode);
    }

    [Fact]
    public async Task AgainstALiveRuntime_CancellingTheBlockAbortsTheRunInSeconds()
    {
        string baseUrl = AgentTestContexts.RequireLiveRuntime();
        (Block block, BlockExecutionContext context, _) = AgentTestContexts.ArrangeLiveAgentBlock(
            _testServiceProvider,
            baseUrl,
            "fuse-cancel",
            static props => props.PromptTemplate =
                "Count slowly from 1 to 400, one number per line, and do not stop early.");

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        string executionId = AgentRunIds.For(context.WorkflowExecutionId!, block.Id, AgentRunIds.WORKER_ROLE, 1, 1);

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new AgentBlockStrategy().ExecuteBlockAsync(block, context, cancellation.Token));

        using var probe = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        string status = string.Empty;
        for (int attempt = 0; attempt < 10 && status != CANCELLED_STATUS; attempt++)
        {
            // Waits on purpose: polls the live sidecar between status checks.
            await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            HttpResponseMessage monitor = await probe.GetAsync(
                $"{RUNS_ROUTE_PREFIX}{executionId}", TestContext.Current.CancellationToken);
            string body = await monitor.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            _output.WriteLine($"monitor after cancel: {body}");
            status = JsonDocument.Parse(body).RootElement.GetProperty("status").GetString() ?? string.Empty;
        }

        Assert.Equal(CANCELLED_STATUS, status);
    }

    [Fact]
    public async Task AgainstALiveRuntime_AbandonedAtTheConsumersPatience_HarvestsTheSettledVerdict()
    {
        string baseUrl = AgentTestContexts.RequireLiveRuntime();
        (Block block, BlockExecutionContext context, _) = AgentTestContexts.ArrangeLiveAgentBlock(
            _testServiceProvider,
            baseUrl,
            "fuse-abandon",
            static props => props.PromptTemplate =
                "Use your Bash tool to run \"sleep 120\", then reply with exactly the word: ready.");

        AgentRuntimeSettings settings = context.PlatformSettings.AgentRuntime;
        int patienceBefore = settings.MaxPollDeadlineMinutes;
        settings.MaxPollDeadlineMinutes = 1;
        try
        {
            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new AgentBlockStrategy().ExecuteBlockAsync(
                    block, context, TestContext.Current.CancellationToken));

            _output.WriteLine(exception.Message);
            Assert.Contains("did not finish within the block deadline", exception.Message);

            var props = (AgentProperties)block.Properties!;
            AgentRunPass pass = Assert.Single(props.ExecutionState!.RunPasses);
            _output.WriteLine($"harvested: status {pass.Status}, cost {pass.EstimatedCostUsd}");
            Assert.NotNull(pass.Status);
            Assert.NotEqual(nameof(AgentRunStatus.Running), pass.Status);
        }
        finally
        {
            settings.MaxPollDeadlineMinutes = patienceBefore;
        }
    }

    [Fact]
    public async Task AgainstALiveRuntime_SidecarRestartMidRun_BuysASecondPassAndSucceeds()
    {
        string baseUrl = AgentTestContexts.RequireLiveRuntime();
        (Block block, BlockExecutionContext context, _) = AgentTestContexts.ArrangeLiveAgentBlock(
            _testServiceProvider,
            baseUrl,
            "fuse-restart",
            static props => props.PromptTemplate =
                "Use your Bash tool to run \"sleep 20\", then reply with exactly the word: ready.");

        AgentRuntimeSettings settings = context.PlatformSettings.AgentRuntime;
        int toleranceBefore = settings.PollErrorTolerance;
        settings.PollErrorTolerance = 10;
        try
        {
            Task blockRun = new AgentBlockStrategy().ExecuteBlockAsync(
                block, context, TestContext.Current.CancellationToken);
            // Waits on purpose: lets the run reach the live sidecar before the restart.
            await Task.Delay(TimeSpan.FromSeconds(6), TestContext.Current.CancellationToken);
            await RestartSidecarAsync();

            await blockRun;

            var props = (AgentProperties)block.Properties!;
            Assert.Equal(2, props.ExecutionState!.RunPasses.Count);
            _output.WriteLine(string.Join(
                " | ",
                props.ExecutionState.RunPasses.Select(
                    static pass => $"{pass.Pass}:{pass.ExecutionId}:{pass.Status}:{pass.FailureSubtype}:{pass.EstimatedCostUsd}")));
            Assert.EndsWith("_w_r1_2", props.ExecutionState.RunPasses[^1].ExecutionId, StringComparison.Ordinal);
            Assert.NotNull(block.BlockResponse);
        }
        finally
        {
            settings.PollErrorTolerance = toleranceBefore;
        }
    }

    private static async Task RestartSidecarAsync()
    {
        // The container name follows the compose project (the runtime checkout's directory
        // name), so a non-default checkout overrides it via the environment.
        string containerName = Environment.GetEnvironmentVariable("INIA_TEST_AGENT_RUNTIME_CONTAINER")
            ?? "agent-runtime-agent-1";

        using Process? restart = Process.Start(
            new ProcessStartInfo("docker", $"restart {containerName}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

        Assert.NotNull(restart);
        await restart.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, restart.ExitCode);
    }
}

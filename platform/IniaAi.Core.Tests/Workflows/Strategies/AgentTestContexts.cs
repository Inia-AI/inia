using ErrorOr;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Core.Tests.Workflows.Strategies;

/// <summary>
/// Stands in for the shared executor layer so a strategy's own decisions — what it declares,
/// how patient it is, how it mints the next pass — are asserted directly instead of inferred
/// from a real run's timing. Suites set <see cref="OnExecute"/> for their default conclusion;
/// settled attempts fan out through <c>OnAttempt</c> exactly as the real executor's do.
/// </summary>
internal sealed class RecordingAgentRunExecutor : IAgentRunExecutor
{
    public AgentRunStartRequest? Request { get; private set; }
    public AgentRunExecutionOptions? Options { get; private set; }
    public int ExecuteCalls { get; private set; }
    public Func<AgentRunStartRequest, ErrorOr<AgentRunConclusion>>? OnExecute { get; set; }

    public Task<ErrorOr<AgentRunConclusion>> ExecuteAsync(
        AgentRunStartRequest request,
        AgentRunExecutionOptions options,
        CancellationToken cancellationToken = default)
    {
        Request = request;
        Options = options;
        ExecuteCalls++;

        ErrorOr<AgentRunConclusion> conclusion = OnExecute is null
            ? throw new InvalidOperationException(
                $"Set {nameof(OnExecute)} to the suite's default conclusion before executing")
            : OnExecute(request);

        if (!conclusion.IsError && options.OnAttempt is not null)
        {
            foreach (AgentRunAttempt attempt in conclusion.Value.Attempts)
            {
                options.OnAttempt(attempt);
            }
        }

        return Task.FromResult(conclusion);
    }

    public Task<bool> AcknowledgeQuietlyAsync(string executionId)
    {
        return Task.FromResult(true);
    }

    public Task CancelQuietlyAsync(string executionId)
    {
        return Task.CompletedTask;
    }

    public Task<bool> SettleAbandonedQuietlyAsync(string executionId, AgentRunTraceAnchor? anchor)
    {
        return Task.FromResult(true);
    }

    public Task<AgentRunMonitor> AbandonQuietlyAsync(string executionId, AgentRunTraceAnchor? anchor)
    {
        return Task.FromResult(new AgentRunMonitor { ExecutionId = executionId, Status = AgentRunStatus.Cancelled });
    }
}

internal static class AgentTestContexts
{
    private const string BASE_URL_ENV = "INIA_TEST_AGENT_RUNTIME";
    private const string MODEL_CREDENTIALS_ENV = "INIA_TEST_AGENT_RUNTIME_MODEL_CREDS";

    /// <summary>
    /// The live gate: a sidecar base URL, plus an explicit opt-in because these runs are billed.
    /// Skips rather than fails so the suite stays green without a sidecar.
    /// </summary>
    public static string RequireLiveRuntime()
    {
        string? baseUrl = Environment.GetEnvironmentVariable(BASE_URL_ENV);
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            Assert.Skip($"Set {BASE_URL_ENV} to a running agent-runtime sidecar base URL to run the live gate.");
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(MODEL_CREDENTIALS_ENV)))
        {
            Assert.Skip($"Set {MODEL_CREDENTIALS_ENV}=1 when the sidecar holds model credentials to run the billed gate.");
        }

        return baseUrl;
    }

    /// <summary>
    /// One live agent block against a real sidecar, shared by every live suite: the cheapest
    /// possible billed run (single-word reply), a fresh identity per call, and the recording
    /// handler for suites that assert what went over the wire.
    /// </summary>
    public static (Block Block, BlockExecutionContext Context, BodyRecordingHandler Handler) ArrangeLiveAgentBlock(
        IServiceProvider testServiceProvider,
        string baseUrl,
        string executionPrefix,
        Action<AgentProperties>? configureProperties = null)
    {
        var handler = new BodyRecordingHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        var liveClient = new AgentRuntimeClient(httpClient, NullLogger<AgentRuntimeClient>.Instance);

        var properties = new AgentProperties
        {
            PromptTemplate = "Reply with exactly the word: ready. Produce nothing else.",
            WorkerSkills =
            [
                new AgentWorkerSkill
                {
                    Name = "ready-check",
                    Description = "Replies with a single word. Use for the live gate.",
                    Content = "Reply with exactly the word: ready.",
                },
            ],
        };
        configureProperties?.Invoke(properties);

        var block = new Block(BlockTypes.Agent, new Section("live", new Workflow("live")))
        {
            Name = "Live agent",
            Properties = properties,
        };

        BlockExecutionContext context = testServiceProvider
            .GetRequiredService<BlockExecutionContext>()
            .WithAgentRuntimeClient(liveClient);
        context.WorkflowExecutionId = $"{executionPrefix}-{Guid.NewGuid():N}"[..24];
        context.TextInputs = [];
        block.SetExecutionState(false, context.TextInputs);

        return (block, context, handler);
    }

    /// <summary>
    /// Records what actually went over the wire, for suites that assert on the request body —
    /// the monitor never echoes it back.
    /// </summary>
    public sealed class BodyRecordingHandler : DelegatingHandler
    {
        public List<string> Bodies { get; } = [];

        public BodyRecordingHandler()
            : base(new HttpClientHandler())
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// Composes a real executor around the given client — the collector must wrap the same
    /// client the executor polls, so swapping the client rebuilds the whole layer around it
    /// instead of carrying the template's. Pass a persister when the run is anchored (an
    /// organization id on the context): an anchored run acknowledges only once its trace pack is
    /// durably committed, so without one every pass settles unacknowledged.
    /// </summary>
    public static BlockExecutionContext WithAgentRuntimeClient(
        this BlockExecutionContext template,
        IAgentRuntimeClient? agentRuntimeClient,
        IAgentRunTracePersister? tracePersister = null)
    {
        return template.WithAgentRunExecutor(
            agentRuntimeClient is null
                ? null
                : new AgentRunExecutor(
                    agentRuntimeClient,
                    new AgentRunPollGate(),
                    NullLogger<AgentRunExecutor>.Instance,
                    new AgentRunTraceCollector(agentRuntimeClient, NullLogger<AgentRunTraceCollector>.Instance),
                    tracePersister));
    }

    public static BlockExecutionContext WithAgentRunExecutor(
        this BlockExecutionContext template,
        IAgentRunExecutor? agentRunExecutor)
    {
        return new BlockExecutionContext(
            (ILogger<BlockExecutionContext>)template.Logger,
            template.ChatConnector,
            template.RagService,
            template.ExchangeRateService,
            template.DocumentAnalysisService,
            template.ExaSearchService,
            template.ModelFamilyResolver,
            template.FileStorageManager,
            template.PdfFormat,
            template.TokenUsageService,
            template.PlatformSettings,
            template.Services,
            agentRunExecutor);
    }
}

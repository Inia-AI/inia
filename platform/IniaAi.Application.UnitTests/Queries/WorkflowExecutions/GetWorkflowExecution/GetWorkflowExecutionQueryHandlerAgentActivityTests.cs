using ErrorOr;
using IniaAi.Application.Common.CreditCosts;
using IniaAi.Application.Common.Services;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Queries.WorkflowExecutions.GetWorkflowExecution;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.Core.Repositories;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Responses.AgentActivity;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Application.UnitTests.Queries.WorkflowExecutions.GetWorkflowExecution;

public class GetWorkflowExecutionQueryHandlerAgentActivityTests : IDisposable
{
    private const string ORG_ID = "org-test";
    private const string USER_ID = "user-test";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;
    private readonly StubFeed _feed = new();
    private readonly GetWorkflowExecutionQueryHandler _handler;

    private sealed class StubFeed : IAgentRunActivityFeed
    {
        public List<AgentRunActivityEntry> Entries { get; } = [];
        public (string OrganizationId, string ExecutionId)? LastSnapshotRequest { get; private set; }

        public void BeginRun(AgentRunActivityRegistration registration)
        {
        }

        public void PublishDigest(string runExecutionId, AgentActivityDigest digest)
        {
        }

        public void ConcludeRun(string runExecutionId)
        {
        }

        public IReadOnlyList<AgentRunActivityEntry> SnapshotForExecution(
            string organizationId,
            string workflowExecutionId)
        {
            LastSnapshotRequest = (organizationId, workflowExecutionId);
            return Entries;
        }

        public IReadOnlyList<AgentRunActivityEntry> SnapshotForConversation(
            string organizationId,
            string conversationId)
        {
            return [];
        }

        public event EventHandler<AgentRunActivityEntry>? EntryChanged
        {
            add
            {
            }

            remove
            {
            }
        }
    }

    public GetWorkflowExecutionQueryHandlerAgentActivityTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();
        _ = services.AddScoped<ICoreWorkflowTypeRepository, CoreWorkflowTypeRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();

        _handler = new GetWorkflowExecutionQueryHandler(
            NullLogger<GetWorkflowExecutionQueryHandler>.Instance,
            _serviceProvider.GetRequiredService<IUserDataUnitOfWork>(),
            _serviceProvider.GetRequiredService<ICoreUnitOfWork>(),
            new NoBaselineReader(),
            new NextWorkflows(_serviceProvider.GetRequiredService<ICoreUnitOfWork>(), new EveryWorkflowAvailable(), new NoCreditCost()),
            _feed);
    }

    [Fact]
    public async Task Handle_AttachesTheLiveFeedSnapshotForThisExecutionAndOrg()
    {
        WorkflowExecution execution = await SeedExecutionAsync();
        _feed.Entries.Add(new AgentRunActivityEntry(
            AgentRunActivityRegistration.ForBlock("run-1", ORG_ID, execution.Id, "block-1", "Agent Block"),
            new AgentActivityDigest
            {
                Outcome = AgentActivityOutcome.Running,
                Beats = [new AgentActivityBeat(4, 0, AgentActivityBeatKind.Hero, "Viewed a page", null)],
            },
            Settled: false));

        ErrorOr<WorkflowExecutionDto> result = await _handler.Handle(
            new GetWorkflowExecutionQuery(ORG_ID, USER_ID, execution.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal((ORG_ID, execution.Id), _feed.LastSnapshotRequest);
        AgentBlockActivityDto activity = Assert.Single(result.Value.AgentActivity ?? []);
        Assert.Equal("block-1", activity.BlockId);
        Assert.Equal("Agent Block", activity.BlockName);
        Assert.False(activity.Settled);
        AgentActivityBeatDto beat = Assert.Single(activity.Activity?.Beats ?? []);
        Assert.Equal("Viewed a page", beat.Label);
    }

    [Fact]
    public async Task Handle_WithoutLiveEntries_LeavesAgentActivityAbsent()
    {
        WorkflowExecution execution = await SeedExecutionAsync();

        ErrorOr<WorkflowExecutionDto> result = await _handler.Handle(
            new GetWorkflowExecutionQuery(ORG_ID, USER_ID, execution.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Null(result.Value.AgentActivity);
    }

    [Fact]
    public async Task Handle_CarriesTheRegistrationsRunIdAndRoundOntoTheActivityRow()
    {
        WorkflowExecution execution = await SeedExecutionAsync();
        _feed.Entries.Add(new AgentRunActivityEntry(
            AgentRunActivityRegistration.ForBlock("run-redo-7", ORG_ID, execution.Id, "block-1", "Agent Block", 3),
            new AgentActivityDigest
            {
                Outcome = AgentActivityOutcome.Running,
                Beats = [new AgentActivityBeat(4, 0, AgentActivityBeatKind.Hero, "Viewed a page", null)],
            },
            Settled: false));

        ErrorOr<WorkflowExecutionDto> result = await _handler.Handle(
            new GetWorkflowExecutionQuery(ORG_ID, USER_ID, execution.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        AgentBlockActivityDto activity = Assert.Single(result.Value.AgentActivity ?? []);
        Assert.Equal("run-redo-7", activity.RunExecutionId);
        Assert.Equal(3, activity.Round);
    }

    [Fact]
    public async Task Handle_CarriesTheLegsRoleOntoTheActivityRow()
    {
        WorkflowExecution execution = await SeedExecutionAsync();
        _feed.Entries.Add(new AgentRunActivityEntry(
            AgentRunActivityRegistration.ForBlock(
                "run-v", ORG_ID, execution.Id, "block-v", "Verifier", 2, AgentRunIds.VERIFIER_ROLE),
            Digest: null,
            Settled: false));
        _feed.Entries.Add(new AgentRunActivityEntry(
            AgentRunActivityRegistration.ForBlock("run-w", ORG_ID, execution.Id, "block-w", "Member"),
            Digest: null,
            Settled: false));

        ErrorOr<WorkflowExecutionDto> result = await _handler.Handle(
            new GetWorkflowExecutionQuery(ORG_ID, USER_ID, execution.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        List<AgentBlockActivityDto> activity = result.Value.AgentActivity ?? [];
        Assert.Equal(
            AgentRunIds.VERIFIER_ROLE_NAME,
            Assert.Single(activity, row => row.RunExecutionId == "run-v").Role);
        Assert.Equal(
            AgentRunIds.WORKER_ROLE_NAME,
            Assert.Single(activity, row => row.RunExecutionId == "run-w").Role);
    }

    /// <summary>
    /// The contract carries the named role, never the run-id letter: the letters are
    /// an internal id alphabet, and a letter the platform has not named passes through verbatim
    /// so the desk can show it as unrecognised instead of silently drawing it as a worker.
    /// </summary>
    [Fact]
    public async Task Handle_MapsEachAllocatedRoleLetterToItsName_AndPassesAnUnknownLetterVerbatim()
    {
        WorkflowExecution execution = await SeedExecutionAsync();
        _feed.Entries.Add(new AgentRunActivityEntry(
            AgentRunActivityRegistration.ForBlock(
                "run-w", ORG_ID, execution.Id, "block-w", "Member", 1, AgentRunIds.WORKER_ROLE),
            Digest: null,
            Settled: false));
        _feed.Entries.Add(new AgentRunActivityEntry(
            AgentRunActivityRegistration.ForBlock(
                "run-v", ORG_ID, execution.Id, "block-v", "Verifier", 1, AgentRunIds.VERIFIER_ROLE),
            Digest: null,
            Settled: false));
        _feed.Entries.Add(new AgentRunActivityEntry(
            AgentRunActivityRegistration.ForBlock(
                "run-x", ORG_ID, execution.Id, "block-x", "Future leg", 1, "x"),
            Digest: null,
            Settled: false));

        ErrorOr<WorkflowExecutionDto> result = await _handler.Handle(
            new GetWorkflowExecutionQuery(ORG_ID, USER_ID, execution.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        List<AgentBlockActivityDto> activity = result.Value.AgentActivity ?? [];
        Assert.Equal("worker", Assert.Single(activity, row => row.RunExecutionId == "run-w").Role);
        Assert.Equal("verifier", Assert.Single(activity, row => row.RunExecutionId == "run-v").Role);
        Assert.Equal("x", Assert.Single(activity, row => row.RunExecutionId == "run-x").Role);
    }

    [Fact]
    public async Task Handle_WithARegistrationThatStatesNoRound_ReportsRoundOne()
    {
        WorkflowExecution execution = await SeedExecutionAsync();
        _feed.Entries.Add(new AgentRunActivityEntry(
            AgentRunActivityRegistration.ForBlock("run-1", ORG_ID, execution.Id, "block-1", "Agent Block"),
            Digest: null,
            Settled: false));

        ErrorOr<WorkflowExecutionDto> result = await _handler.Handle(
            new GetWorkflowExecutionQuery(ORG_ID, USER_ID, execution.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        AgentBlockActivityDto activity = Assert.Single(result.Value.AgentActivity ?? []);
        Assert.Equal(1, activity.Round);
        Assert.Equal("run-1", activity.RunExecutionId);
    }

    /// <summary>
    /// A verification group buys a run per member per round, so the same block id appears in the
    /// feed once per leg. The block id alone cannot key a desk row — without the run id and the
    /// round the two legs read as one block flickering between them.
    /// </summary>
    [Fact]
    public async Task Handle_WithTwoRoundsOfOneBlock_ReportsARowPerLeg()
    {
        WorkflowExecution execution = await SeedExecutionAsync();
        _feed.Entries.Add(new AgentRunActivityEntry(
            AgentRunActivityRegistration.ForBlock("run-first", ORG_ID, execution.Id, "block-1", "Agent Block"),
            new AgentActivityDigest
            {
                Outcome = AgentActivityOutcome.Failed,
                Beats = [new AgentActivityBeat(4, 0, AgentActivityBeatKind.Check, "Checked a total", null)],
            },
            Settled: true));
        _feed.Entries.Add(new AgentRunActivityEntry(
            AgentRunActivityRegistration.ForBlock("run-redo", ORG_ID, execution.Id, "block-1", "Agent Block", 2),
            new AgentActivityDigest
            {
                Outcome = AgentActivityOutcome.Running,
                Beats = [new AgentActivityBeat(2, 0, AgentActivityBeatKind.Hero, "Ran the code again", null)],
            },
            Settled: false));

        ErrorOr<WorkflowExecutionDto> result = await _handler.Handle(
            new GetWorkflowExecutionQuery(ORG_ID, USER_ID, execution.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        List<AgentBlockActivityDto> activity = result.Value.AgentActivity ?? [];
        Assert.Equal(2, activity.Count);

        AgentBlockActivityDto firstPass = Assert.Single(activity, row => row.RunExecutionId == "run-first");
        AgentBlockActivityDto redo = Assert.Single(activity, row => row.RunExecutionId == "run-redo");

        Assert.Equal("block-1", firstPass.BlockId);
        Assert.Equal("block-1", redo.BlockId);
        Assert.Equal(1, firstPass.Round);
        Assert.Equal(2, redo.Round);
        Assert.True(firstPass.Settled);
        Assert.False(redo.Settled);
        Assert.Equal("Checked a total", Assert.Single(firstPass.Activity?.Beats ?? []).Label);
        Assert.Equal("Ran the code again", Assert.Single(redo.Activity?.Beats ?? []).Label);
    }

    private async Task<WorkflowExecution> SeedExecutionAsync()
    {
        var execution = new WorkflowExecution
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = "workflow-type-1",
        };

        _ = await _context.WorkflowExecutions.AddAsync(execution, TestContext.Current.CancellationToken);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        return execution;
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }
}

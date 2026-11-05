using IniaAi.Common.Interfaces.Services.Runs;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Workflows.Executions;
using IniaAi.Core.Workflows.Outputs.Interfaces;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IniaAi.Core.Tests.Workflows.Executions;

/// <summary>
/// The host-loss sweep fails the active runs of a process start that stopped heartbeating, as
/// HostLost, and settles each. A live start's runs, this process's own runs, runs with no recorded
/// start, runs whose start never wrote its row, runs of a start the janitor's lifetime has outlived,
/// and finished runs are left alone, and so is every run while the sweep's own heartbeat is new.
/// </summary>
public class HostLossSweepTests : IDisposable
{
    private const string ORG_ID = "TestOrg";

    // A database per test, not per class: the sweep judges every run in it, and the guard test
    // leaves a lost host's run in Running on purpose.
    private readonly WorkflowsFixture<HostLossSweepTests> _fixture = new();
    private readonly IServiceProvider _testServiceProvider;
    private readonly IServiceScope _serviceScope;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly HostBootIdentity _ownBoot = new();

    private readonly RecordingRunSettlement _settlement = new();
    private readonly HostLossSweep _sweep;

    public HostLossSweepTests(ITestOutputHelper testOutputHelper)
    {
        _testServiceProvider = _fixture.BuildTestServiceProvider(
            testOutputHelper,
            services => services.AddSingleton<IRunSettlement>(_settlement));
        _serviceScope = _testServiceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        _ownBoot.HeartbeatSucceeded(DateTimeOffset.UtcNow - HostLossSweep.HEALTHY_BEFORE_JUDGING);
        _userDataUnitOfWork = _serviceScope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
        _sweep = new HostLossSweep(
            _userDataUnitOfWork,
            _testServiceProvider.GetRequiredService<IWorkflowOutputService>(),
            _ownBoot,
            Options.Create(new WorkflowExecutionJanitorSettings { MaxLifetimeHours = 12 }),
            _testServiceProvider.GetRequiredService<ILogger<HostLossSweep>>());
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _serviceScope.Dispose();
        _fixture.Dispose();
    }

    [Fact]
    public async Task ARunOfAStoppedHost_FailsAsHostLost_AndIsSettled()
    {
        HostBoot stopped = await SeedBootAsync(lastSeen: DateTime.UtcNow.AddMinutes(-3));
        WorkflowExecution running = await SeedExecutionAsync(WorkflowState.Running, stopped.Id);
        WorkflowExecution queued = await SeedExecutionAsync(WorkflowState.Queued, stopped.Id);

        int marked = await _sweep.SweepOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, marked);
        foreach (WorkflowExecution execution in new[] { running, queued })
        {
            WorkflowExecution? reloaded = await ReadFreshAsync(execution.Id);
            Assert.Equal(WorkflowState.Failed, reloaded?.WorkflowState);
            Assert.Equal(WorkflowExecutionFailureKinds.HOST_LOST, reloaded?.FailureKind);
        }

        Assert.Equal(new[] { (running.Id, ORG_ID), (queued.Id, ORG_ID) }.Order(), _settlement.Settled.Order());
    }

    [Fact]
    public async Task RunsTheSweepCannotProveLost_AreLeftAlone()
    {
        HostBoot live = await SeedBootAsync(lastSeen: DateTime.UtcNow.AddSeconds(-20));
        HostBoot stopped = await SeedBootAsync(lastSeen: DateTime.UtcNow.AddMinutes(-10));
        HostBoot longGone = await SeedBootAsync(lastSeen: DateTime.UtcNow.AddHours(-13));
        WorkflowExecution onLiveHost = await SeedExecutionAsync(WorkflowState.Running, live.Id);
        WorkflowExecution onThisHost = await SeedExecutionAsync(WorkflowState.Running, _ownBoot.Id);
        WorkflowExecution withNoHost = await SeedExecutionAsync(WorkflowState.Running, hostBootId: null);
        WorkflowExecution onAHostWithNoRow = await SeedExecutionAsync(WorkflowState.Running, Guid.NewGuid().ToString());
        WorkflowExecution finished = await SeedExecutionAsync(WorkflowState.Draft, stopped.Id);
        WorkflowExecution janitorsToFail = await SeedExecutionAsync(WorkflowState.Running, longGone.Id);

        int marked = await _sweep.SweepOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, marked);
        foreach (WorkflowExecution execution in new[] { onLiveHost, onThisHost, withNoHost, onAHostWithNoRow, finished, janitorsToFail })
        {
            Assert.Equal(execution.WorkflowState, (await ReadFreshAsync(execution.Id))?.WorkflowState);
        }

        Assert.Empty(_settlement.Settled);
    }

    [Fact]
    public async Task UntilItsOwnHeartbeatHasHeldAWhile_TheSweepJudgesNoOtherHost()
    {
        HostBoot stopped = await SeedBootAsync(lastSeen: DateTime.UtcNow.AddMinutes(-3));
        WorkflowExecution running = await SeedExecutionAsync(WorkflowState.Running, stopped.Id);
        _ownBoot.HeartbeatFailed();
        _ownBoot.HeartbeatSucceeded(DateTimeOffset.UtcNow);

        int marked = await _sweep.SweepOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, marked);
        Assert.Equal(WorkflowState.Running, (await ReadFreshAsync(running.Id))?.WorkflowState);
    }

    private async Task<HostBoot> SeedBootAsync(DateTime lastSeen)
    {
        var boot = new HostBoot
        {
            Application = "IniaAi.WebApi",
            Machine = "a-container",
            StartedAt = lastSeen.AddHours(-1),
            LastSeenAt = lastSeen,
        };
        await _userDataUnitOfWork.Repository<HostBoot>().AddAsync(boot);
        _ = await _userDataUnitOfWork.CompleteAsync(TestContext.Current.CancellationToken);

        return boot;
    }

    private async Task<WorkflowExecution> SeedExecutionAsync(WorkflowState state, string? hostBootId)
    {
        var execution = new WorkflowExecution
        {
            OrganizationId = ORG_ID,
            Title = "Host loss test",
            WorkflowState = state,
            HostBootId = hostBootId,
        };
        await _userDataUnitOfWork.WorkflowExecutions.AddAsync(execution);
        _ = await _userDataUnitOfWork.CompleteAsync(TestContext.Current.CancellationToken);

        return execution;
    }

    private async Task<WorkflowExecution?> ReadFreshAsync(string executionId)
    {
        using IServiceScope scope = _testServiceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>()
            .WorkflowExecutions.GetByIdAsync(executionId, TestContext.Current.CancellationToken);
    }
}

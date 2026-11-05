using IniaAi.Core.Tests.Workflows.Scheduling.Helpers;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Workflows.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Tests.Workflows;

/// <summary>
/// A module registers the strategy for its own block type. A registration for a type the handler
/// already runs is a wiring mistake, so building the handler fails instead of replacing a strategy.
/// </summary>
public class BlockStrategyRegistrationTests : IClassFixture<WorkflowsFixture<BlockStrategyRegistrationTests>>
{
    private readonly WorkflowsFixture<BlockStrategyRegistrationTests> _fixture;
    private readonly ITestOutputHelper _testOutputHelper;

    public BlockStrategyRegistrationTests(
        WorkflowsFixture<BlockStrategyRegistrationTests> fixture,
        ITestOutputHelper testOutputHelper)
    {
        _fixture = fixture;
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public void ARegistrationForATypeTheHandlerAlreadyRuns_FailsTheHandlerBuild()
    {
        IServiceProvider sp = _fixture.BuildTestServiceProvider(
            _testOutputHelper,
            services => services.AddSingleton(new BlockStrategyRegistration(BlockTypes.Merge, new MergeStrategy())),
            withoutClosedModules: true);

        _ = Assert.Throws<InvalidOperationException>(() => TestWorkflowHandlers.BuildWithScheduler(sp, maxBlockParallelism: 1));
    }
}

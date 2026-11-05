using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Tests.Workflows;

/// <summary>
/// A strategy that catches its own failure and reports it on the block's execution state fails
/// the block. It used to complete over the error, so the run showed "Completed".
/// </summary>
public sealed class StrategyReportedFailureTests : IClassFixture<WorkflowsFixture<StrategyReportedFailureTests>>, IDisposable
{
    private const string ORGANIZATION_ID = "strategy-failure-org";

    private readonly WorkflowHandler _workflowHandler;
    private readonly IServiceScope _scope;
    private readonly IUserDataUnitOfWork _userData;

    public StrategyReportedFailureTests(WorkflowsFixture<StrategyReportedFailureTests> fixture, ITestOutputHelper output)
    {
        IServiceProvider serviceProvider = fixture.BuildTestServiceProvider(output);
        _workflowHandler = serviceProvider.GetRequiredService<WorkflowHandler>();
        _scope = serviceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        _userData = _scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
    }

    [Fact]
    public async Task ABlockWhoseStrategyReportsAnError_FailsWithThatError()
    {
        Dictionary<string, Block> blocks = await RunAsync(BlockTypes.ExchangeRate, "Convert_FX",
            new ExchangeRateProperties { FromCurrency = null, ToCurrency = "EUR", UseCurrentDate = true });

        Block exchange = blocks["Convert_FX"];
        Assert.Equal(ExecutionStatus.Failed, exchange.ExecutionState?.Status);
        Assert.Equal("Missing currency codes", exchange.ExecutionState?.Error);

        WorkflowExecutionLog log = await LogAsync("Convert_FX");
        Assert.Equal(BlockExecutionStatus.Failed, log.Status);
        Assert.Null(log.RefusalCategory);
    }

    [Fact]
    public async Task ASearchWithNothingToSearch_IsSkipped_NotFailed()
    {
        Dictionary<string, Block> blocks = await RunAsync(BlockTypes.IntelligentSearch, "Search_Peers",
            new IntelligentSearchProperties { SearchTermTemplate = null });

        Assert.Equal(ExecutionStatus.Skipped, blocks["Search_Peers"].ExecutionState?.Status);
        Assert.Equal(BlockExecutionStatus.Skipped, (await LogAsync("Search_Peers")).Status);
    }

    private async Task<Dictionary<string, Block>> RunAsync(BlockTypes type, string name, BlockProperties properties)
    {
        var workflow = new Workflow();
        var section = new Section("Section", workflow);
        _ = new Block(type, section) { Name = name, ReplacementTag = "tag", Properties = properties };

        string userId = Guid.NewGuid().ToString();
        await _workflowHandler.CreateRootOutputAsync(_userData, null, workflow, ORGANIZATION_ID, userId);
        Workflow executed = await _workflowHandler.ExecuteWorkflowAsync(
            workflow, [], ORGANIZATION_ID, userId, _userData, inputs: [], cancellationToken: TestContext.Current.CancellationToken);

        return executed.Sections.SelectMany(static s => s.Blocks).ToDictionary(static block => block.Name);
    }

    private async Task<WorkflowExecutionLog> LogAsync(string blockName)
    {
        string executionId = _workflowHandler.WorkflowExecutionId
            ?? throw new InvalidOperationException("The run has no execution.");

        return Assert.Single(await _userData.Repository<WorkflowExecutionLog>()
            .FindAsync(row => row.WorkflowExecutionId == executionId && row.BlockName == blockName));
    }

    public void Dispose()
    {
        _scope.Dispose();
    }
}

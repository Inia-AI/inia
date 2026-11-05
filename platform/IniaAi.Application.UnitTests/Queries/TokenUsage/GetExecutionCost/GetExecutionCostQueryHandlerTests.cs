using ErrorOr;
using IniaAi.Application.Queries.TokenUsage.GetExecutionCost;
using IniaAi.Application.UnitTests.Queries.AgentRuns.ExecutionAgentRunFiles;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Responses.TokenUsage;
using Microsoft.Extensions.DependencyInjection;
using static IniaAi.Application.UnitTests.Queries.AgentRuns.ExecutionAgentRunFiles.RunEvidenceTestData;
using TokenExecutionContext = IniaAi.Domain.Entities.TokenUsage.Enums.ExecutionContext;
using TokenUsageComponentEntity = IniaAi.Domain.Entities.TokenUsage.TokenUsageComponent;
using TokenUsageEntity = IniaAi.Domain.Entities.TokenUsage.TokenUsage;

namespace IniaAi.Application.UnitTests.Queries.TokenUsage.GetExecutionCost;

public class GetExecutionCostQueryHandlerTests : IDisposable
{
    private const string MODEL = "claude-haiku-5-5";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;
    private readonly IUserDataUnitOfWork _unitOfWork;

    public GetExecutionCostQueryHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();
        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();
        _unitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
    }

    private static TokenUsageEntity Usage(
        string organizationId,
        string? executionId,
        string? blockId,
        UsageType usageType,
        params TokenUsageComponentEntity[] components)
    {
        return new TokenUsageEntity
        {
            OrganizationId = organizationId,
            WorkflowExecutionId = executionId,
            BlockId = blockId,
            UsageType = usageType,
            ExecutionContext = TokenExecutionContext.WorkflowExecution,
            BlockType = blockId is null ? null : BlockTypes.AiQuery,
            Provider = "aws-bedrock",
            Family = "anthropic",
            ModelId = MODEL,
            Components = components,
        };
    }

    private static TokenUsageComponentEntity Component(PricingComponentType type, int units, decimal cost)
    {
        return new TokenUsageComponentEntity { ComponentType = type, Units = units, Cost = cost };
    }

    private async Task SeedAsync(params TokenUsageEntity[] usages)
    {
        await _context.TokenUsages.AddRangeAsync(usages, TestContext.Current.CancellationToken);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();
    }

    private Task<ErrorOr<ExecutionCostDto>> CostAsync(string organizationId, string userId, string executionId, bool superAdmin)
    {
        return new GetExecutionCostQueryHandler(_unitOfWork).Handle(
            new GetExecutionCostQuery(organizationId, userId, executionId) { CallerIsSuperAdmin = superAdmin },
            TestContext.Current.CancellationToken);
    }

    private async Task<SeededRun> SeedCostedRunAsync(string startedBy = ANALYST_ID)
    {
        await SeedUsersAsync(_context);
        SeededRun run = await SeedRunAsync(_context, ORG_ID, startedBy);
        SeededRun otherRun = await SeedRunAsync(_context, ORG_ID, startedBy);

        await SeedAsync(
            Usage(ORG_ID, run.Execution.Id, run.Agent.Id, UsageType.LlmAgentRun,
                Component(PricingComponentType.AgentRunSpend, 1, 0.30m)),
            Usage(ORG_ID, run.Execution.Id, run.Agent.Id, UsageType.LlmAgentRun,
                Component(PricingComponentType.Input, 1000, 0m),
                Component(PricingComponentType.Output, 200, 0m),
                Component(PricingComponentType.CacheRead, 5000, 0m),
                Component(PricingComponentType.CacheWrite5m, 700, 0m)),
            Usage(ORG_ID, executionId: null, run.Merge.Id, UsageType.LlmBlockExecution,
                Component(PricingComponentType.Input, 100, 0.01m),
                Component(PricingComponentType.Output, 10, 0.02m)),
            Usage(ORG_ID, run.Execution.Id, blockId: null, UsageType.LlmOcrTranscription,
                Component(PricingComponentType.Input, 50, 0.005m)),
            Usage(ORG_ID, run.Execution.Id, blockId: null, UsageType.LlmOverviewGeneration,
                Component(PricingComponentType.Input, 50, 0m)),
            Usage(ORG_ID, otherRun.Execution.Id, otherRun.Agent.Id, UsageType.LlmAgentRun,
                Component(PricingComponentType.AgentRunSpend, 1, 9m)),
            Usage(OTHER_ORG_ID, run.Execution.Id, run.Agent.Id, UsageType.LlmAgentRun,
                Component(PricingComponentType.AgentRunSpend, 1, 7m)));

        return run;
    }

    [Fact]
    public async Task Cost_ForASuperAdmin_SumsTheRunsRowsPerBlockAndOutsideBlocks()
    {
        SeededRun run = await SeedCostedRunAsync();

        ErrorOr<ExecutionCostDto> result = await CostAsync(ORG_ID, "super-admin", run.Execution.Id, superAdmin: true);

        Assert.False(result.IsError);
        ExecutionCostDto cost = result.Value;
        Assert.Equal(0.335m, cost.TotalCost);
        Assert.Equal(5, cost.UsageRecordCount);
        Assert.Equal(1, cost.UnpricedRecordCount);

        ExecutionCostLineDto agent = cost.Blocks[0];
        Assert.Equal(run.Agent.Id, agent.BlockId);
        Assert.Equal("Agent", agent.Name);
        Assert.Equal("agent_tag", agent.ReplacementTag);
        Assert.Equal(0.30m, agent.Cost);
        Assert.Equal(2, agent.Calls);
        Assert.Equal(1000, agent.InputTokens);
        Assert.Equal(200, agent.OutputTokens);
        Assert.Equal(5000, agent.CacheReadTokens);
        Assert.Equal(700, agent.CacheWriteTokens);
        Assert.Equal([MODEL], agent.Models);

        ExecutionCostLineDto merge = cost.Blocks[1];
        Assert.Equal(run.Merge.Id, merge.BlockId);
        Assert.Equal(0.03m, merge.Cost);

        Assert.Equal(2, cost.Other.Count);
        Assert.Equal(nameof(UsageType.LlmOcrTranscription), cost.Other[0].Name);
        Assert.Equal(0.005m, cost.Other[0].Cost);
    }

    [Fact]
    public async Task Cost_ForTheServiceUserThatStartedTheRun_IsReturned()
    {
        SeededRun run = await SeedCostedRunAsync(startedBy: SERVICE_USER_ID);

        ErrorOr<ExecutionCostDto> result = await CostAsync(ORG_ID, SERVICE_USER_ID, run.Execution.Id, superAdmin: false);

        Assert.False(result.IsError);
        Assert.Equal(0.335m, result.Value.TotalCost);
    }

    [Fact]
    public async Task Cost_ForAnyOtherCaller_IsForbidden()
    {
        SeededRun run = await SeedCostedRunAsync(startedBy: ANALYST_ID);

        ErrorOr<ExecutionCostDto> analyst = await CostAsync(ORG_ID, ANALYST_ID, run.Execution.Id, superAdmin: false);
        ErrorOr<ExecutionCostDto> serviceUser = await CostAsync(ORG_ID, SERVICE_USER_ID, run.Execution.Id, superAdmin: false);

        Assert.Equal(ErrorType.Forbidden, analyst.FirstError.Type);
        Assert.Equal(ErrorType.Forbidden, serviceUser.FirstError.Type);
    }

    [Fact]
    public async Task Cost_FromAnotherOrganization_IsNotFound()
    {
        SeededRun run = await SeedCostedRunAsync();

        ErrorOr<ExecutionCostDto> result = await CostAsync(OTHER_ORG_ID, "super-admin", run.Execution.Id, superAdmin: true);

        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }
}

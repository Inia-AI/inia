using ErrorOr;
using IniaAi.Application.Queries.TokenUsage.GetOrganizationUsageDashboard;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.WebApi.Contracts.Responses.TokenUsage;
using Microsoft.EntityFrameworkCore;
using TokenUsageEntity = IniaAi.Domain.Entities.TokenUsage.TokenUsage;
using TokenUsageComponentEntity = IniaAi.Domain.Entities.TokenUsage.TokenUsageComponent;
using TokenExecutionContext = IniaAi.Domain.Entities.TokenUsage.Enums.ExecutionContext;

namespace IniaAi.Application.UnitTests.Queries.TokenUsage.GetOrganizationUsageDashboard;

/// <summary>
/// The dashboard ranks by tokens, counting only <c>Input</c> and <c>Output</c> units. An agent
/// run whose usage record carried spend alone therefore ranked nowhere; the trace-derived token
/// records are what put agent-heavy workflows back into the ranked lists.
/// </summary>
public class GetOrganizationUsageDashboardQueryHandlerTests : IDisposable
{
    private const string ORGANIZATION_ID = "org-1";
    private const string USER_ID = "user-1";
    private const string AGENT_WORKFLOW_TYPE_ID = "wt-agent";
    private const string QUERY_WORKFLOW_TYPE_ID = "wt-query";
    private const string AGENT_MODEL_ID = "claude-sonnet-4-6";

    private readonly UserDataDbContext _userData;
    private readonly CoreDbContext _core;

    public GetOrganizationUsageDashboardQueryHandlerTests()
    {
        string databaseName = $"usage-dashboard-{Guid.NewGuid()}";
        _userData = new UserDataDbContext(new DbContextOptionsBuilder<UserDataDbContext>()
            .UseInMemoryDatabase($"{databaseName}-userdata").Options);
        _core = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase($"{databaseName}-core").Options);
    }

    public void Dispose()
    {
        _userData.Dispose();
        _core.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<OrganizationUsageDashboardDto> DashboardAsync()
    {
        var handler = new GetOrganizationUsageDashboardQueryHandler(_userData, _core);
        ErrorOr<OrganizationUsageDashboardDto> dashboard = await handler.Handle(
            new GetOrganizationUsageDashboardQuery(ORGANIZATION_ID, USER_ID),
            TestContext.Current.CancellationToken);

        Assert.False(dashboard.IsError);

        return dashboard.Value;
    }

    private async Task SeedAsync(params TokenUsageEntity[] usages)
    {
        await _userData.TokenUsages.AddRangeAsync(usages, TestContext.Current.CancellationToken);
        _ = await _userData.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static TokenUsageEntity Usage(
        UsageType usageType,
        string workflowTypeId,
        string modelId,
        params TokenUsageComponentEntity[] components)
    {
        return new TokenUsageEntity
        {
            OrganizationId = ORGANIZATION_ID,
            UserId = USER_ID,
            WorkflowTypeId = workflowTypeId,
            WorkflowExecutionId = $"exec-{workflowTypeId}",
            UsageType = usageType,
            ExecutionContext = TokenExecutionContext.WorkflowExecution,
            BlockType = usageType == UsageType.LlmAgentRun ? BlockTypes.Agent : BlockTypes.AiQuery,
            Provider = "agent-runtime",
            Family = "anthropic",
            ModelId = modelId,
            Components = components,
        };
    }

    private static TokenUsageComponentEntity Component(PricingComponentType componentType, int units, decimal cost = 0m)
    {
        return new TokenUsageComponentEntity { ComponentType = componentType, Units = units, Cost = cost };
    }

    [Fact]
    public async Task Handle_AgentRunTokens_RankTheAgentWorkflowAboveASmallerQueryWorkflow()
    {
        await SeedAsync(
            Usage(
                UsageType.LlmAgentRun, AGENT_WORKFLOW_TYPE_ID, AGENT_MODEL_ID,
                Component(PricingComponentType.AgentRunSpend, 1, 0.08m)),
            Usage(
                UsageType.LlmAgentRun, AGENT_WORKFLOW_TYPE_ID, AGENT_MODEL_ID,
                Component(PricingComponentType.Input, 900),
                Component(PricingComponentType.Output, 100),
                Component(PricingComponentType.CacheRead, 5_000)),
            Usage(
                UsageType.LlmBlockExecution, QUERY_WORKFLOW_TYPE_ID, "claude-haiku-4-5",
                Component(PricingComponentType.Input, 50, 0.001m),
                Component(PricingComponentType.Output, 10, 0.001m)));

        OrganizationUsageDashboardDto dashboard = await DashboardAsync();

        UsageByWorkflowTypeDto topWorkflow = dashboard.ByWorkflowType[0];
        Assert.Equal(AGENT_WORKFLOW_TYPE_ID, topWorkflow.WorkflowTypeId);
        Assert.Equal(1_000, topWorkflow.TotalTokens);
        Assert.Equal(0.08m, topWorkflow.TotalCost);

        UsageByTypeDto topUsageType = dashboard.ByUsageType[0];
        Assert.Equal(nameof(UsageType.LlmAgentRun), topUsageType.UsageType);
        Assert.Equal(1_000, topUsageType.TotalTokens);

        UsageByUserDto onlyUser = Assert.Single(dashboard.ByUser);
        Assert.Equal(1_060, onlyUser.TotalTokens);

        Assert.Equal(950, dashboard.AllTime.TotalInputTokens);
        Assert.Equal(110, dashboard.AllTime.TotalOutputTokens);
        Assert.Equal(5_000, dashboard.AllTime.TotalCacheReadTokens);
    }

    [Fact]
    public async Task Handle_AgentSpendWithoutTokens_ContributesCostButRanksNoTokens()
    {
        await SeedAsync(
            Usage(
                UsageType.LlmAgentRun, AGENT_WORKFLOW_TYPE_ID, AGENT_MODEL_ID,
                Component(PricingComponentType.AgentRunSpend, 1, 0.08m)));

        OrganizationUsageDashboardDto dashboard = await DashboardAsync();

        UsageByWorkflowTypeDto agentWorkflow = Assert.Single(dashboard.ByWorkflowType);
        Assert.Equal(0, agentWorkflow.TotalTokens);
        Assert.Equal(0.08m, agentWorkflow.TotalCost);
    }

    [Fact]
    public async Task Handle_IntakeScreeningTokens_CountAsLlmTokens_OnTheirOwnUsageLine()
    {
        await SeedAsync(
            Usage(
                UsageType.LlmIntakeScreening, QUERY_WORKFLOW_TYPE_ID, "claude-haiku-4-5",
                Component(PricingComponentType.Input, 300, 0.0003m),
                Component(PricingComponentType.Output, 40, 0.0002m)),
            Usage(
                UsageType.LlmOcrTranscription, QUERY_WORKFLOW_TYPE_ID, "claude-sonnet-4-6",
                Component(PricingComponentType.Input, 1_000, 0.003m),
                Component(PricingComponentType.Output, 200, 0.003m)));

        OrganizationUsageDashboardDto dashboard = await DashboardAsync();

        Assert.InRange((int)UsageType.LlmIntakeScreening, 0, 19);
        Assert.Equal(1_540, Assert.Single(dashboard.DailyTrend).LlmTokens);
        UsageByTypeDto screening = Assert.Single(dashboard.ByUsageType, usage => usage.UsageType == nameof(UsageType.LlmIntakeScreening));
        Assert.Equal(340, screening.TotalTokens);
    }
}

using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Domain.Entities.TokenUsage;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TokenExecutionContext = IniaAi.Domain.Entities.TokenUsage.Enums.ExecutionContext;

namespace IniaAi.Infrastructure.Tests.Services;

/// <summary>
/// Some producers price their own work. The agent runtime meters a run's spend itself and the
/// platform has no token split to re-derive it from, so a supplied cost is recorded as given.
/// </summary>
public class TokenUsageReportedCostTests : IDisposable
{
    private const string ORGANIZATION_ID = "org-1";
    private const string PROVIDER = "agent-runtime";
    private const string MODEL_ID = "us.anthropic.claude-haiku-4-5-20251001-v1:0";

    private readonly UserDataDbContext _userData;
    private readonly CoreDbContext _core;
    private readonly ServiceProvider _services;

    public TokenUsageReportedCostTests()
    {
        string databaseName = $"reported-cost-{Guid.NewGuid()}";
        _userData = new UserDataDbContext(new DbContextOptionsBuilder<UserDataDbContext>()
            .UseInMemoryDatabase($"{databaseName}-userdata").Options);
        _core = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase($"{databaseName}-core").Options);

        // Singletons, not scoped: the service records inside its own DI scope, and a scoped
        // registration would let that scope's disposal take these contexts with it.
        var services = new ServiceCollection();
        _ = services.AddSingleton<IUserDataDbContext>(_userData);
        _ = services.AddSingleton<ICoreDbContext>(_core);
        _services = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        _userData.Dispose();
        _core.Dispose();
        GC.SuppressFinalize(this);
    }

    private TokenUsageService CreateService()
    {
        return new TokenUsageService(
            _services,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<TokenUsageService>.Instance);
    }

    private static LlmUsageRecord Record(params UsageComponent[] components)
    {
        return new LlmUsageRecord
        {
            OrganizationId = ORGANIZATION_ID,
            UsageType = UsageType.LlmAgentRun,
            ExecutionContext = TokenExecutionContext.WorkflowExecution,
            Provider = PROVIDER,
            Family = "anthropic",
            ModelId = MODEL_ID,
            Components = components,
            Description = "Agent block 'Report'",
        };
    }

    [Fact]
    public async Task ASuppliedCost_IsRecordedWithoutAnyPricingRow_AndReturned()
    {
        decimal recorded = await CreateService().RecordLlmUsageAsync(
            Record(new UsageComponent
            {
                ComponentType = PricingComponentType.AgentRunSpend,
                Units = 2,
                CostUsd = 0.1234m,
            }),
            TestContext.Current.CancellationToken);

        Assert.Equal(0.1234m, recorded);

        TokenUsage usage = await _userData.TokenUsages
            .Include(row => row.Components)
            .SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UsageType.LlmAgentRun, usage.UsageType);
        Assert.Null(usage.ModelPricingId);
        TokenUsageComponent component = Assert.Single(usage.Components);
        Assert.Equal(PricingComponentType.AgentRunSpend, component.ComponentType);
        Assert.Equal(2, component.Units);
        Assert.Equal(0.1234m, component.Cost);
    }

    private async Task<ModelPricing> SeedPricingAsync()
    {
        var pricing = new ModelPricing
        {
            Provider = PROVIDER,
            ModelId = MODEL_ID,
            DisplayName = "Agent runtime",
            PricingType = PricingType.Llm,
            IsActive = true,
        };

        _ = await _core.ModelPricings.AddAsync(pricing, TestContext.Current.CancellationToken);
        _ = await _core.SaveChangesAsync(TestContext.Current.CancellationToken);

        return pricing;
    }

    [Fact]
    public async Task NoComponentsAtAll_StillStampsThePricingItWasRecordedUnder()
    {
        ModelPricing pricing = await SeedPricingAsync();

        await CreateService().RecordLlmUsageAsync(Record(), TestContext.Current.CancellationToken);

        TokenUsage usage = await _userData.TokenUsages
            .Include(row => row.Components)
            .SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(pricing.Id, usage.ModelPricingId);
        Assert.Empty(usage.Components);
    }

    [Fact]
    public async Task EverySuppliedCost_SkipsThePricingLookupEvenWhenARowExists()
    {
        _ = await SeedPricingAsync();

        await CreateService().RecordLlmUsageAsync(
            Record(new UsageComponent
            {
                ComponentType = PricingComponentType.AgentRunSpend,
                Units = 1,
                CostUsd = 0.5m,
            }),
            TestContext.Current.CancellationToken);

        TokenUsage usage = await _userData.TokenUsages
            .Include(row => row.Components)
            .SingleAsync(TestContext.Current.CancellationToken);

        Assert.Null(usage.ModelPricingId);
    }

    [Fact]
    public async Task AComponentWithoutASuppliedCost_StillFallsBackToPricing_AndReturnsWhatItPriced()
    {
        decimal recorded = await CreateService().RecordLlmUsageAsync(
            Record(new UsageComponent { ComponentType = PricingComponentType.Input, Units = 1000 }),
            TestContext.Current.CancellationToken);

        Assert.Equal(0m, recorded);

        TokenUsage usage = await _userData.TokenUsages
            .Include(row => row.Components)
            .SingleAsync(TestContext.Current.CancellationToken);

        TokenUsageComponent component = Assert.Single(usage.Components);
        Assert.Equal(0m, component.Cost);
    }
}

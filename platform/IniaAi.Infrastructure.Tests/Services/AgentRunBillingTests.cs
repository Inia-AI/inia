using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Core.Services.TokenUsage;
using IniaAi.Domain.Entities.TokenUsage;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using TokenExecutionContext = IniaAi.Domain.Entities.TokenUsage.Enums.ExecutionContext;

namespace IniaAi.Infrastructure.Tests.Services;

/// <summary>
/// An agent run is billed from its tokens at the pricing table's list prices, request by
/// request, never from the runtime's own figure while a pricing row covers the tokens
/// (the runtime passed on the model SDK's figure, which priced Claude Haiku 5.5 at Claude Opus 5.5
/// rates). The pricing rows are the seeded defaults, so these tests pin the seeded rates too.
/// </summary>
public class AgentRunBillingTests : IDisposable
{
    private const string ORGANIZATION_ID = "org-1";
    private const string AGENT_RUNTIME_PROVIDER = "agent-runtime";
    private const string HAIKU_5_5_REQUESTED = "us.anthropic.claude-haiku-5-5";
    private const string HAIKU_5_5 = "claude-haiku-5-5";
    private const string SONNET_5_5 = "claude-sonnet-5-5";
    private const string SONNET_5 = "claude-sonnet-5";
    private const string OPUS_5_5 = "claude-opus-5-5";
    private const string COST_SOURCE_FIELD = "costSource";
    private const string RUNTIME_COST_FIELD = "runtimeCostUsd";
    private const long ONE_MILLION = 1_000_000;

    // Bedrock bills the us. profiles these runs call at 1.1 times the list price.
    private const decimal US_PROFILE = 1.10m;

    private readonly UserDataDbContext _userData;
    private readonly CoreDbContext _core;
    private readonly ServiceProvider _services;
    private readonly TokenUsageService _tokenUsage;

    public AgentRunBillingTests()
    {
        string databaseName = $"agent-billing-{Guid.NewGuid()}";
        _userData = new UserDataDbContext(new DbContextOptionsBuilder<UserDataDbContext>()
            .UseInMemoryDatabase($"{databaseName}-userdata").Options);
        _core = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase($"{databaseName}-core").Options);
        _core.ModelPricings.AddRange(ModelPricingSeedingService.GetDefaultPricings());
        _ = _core.SaveChanges();

        var services = new ServiceCollection();
        _ = services.AddSingleton<IUserDataDbContext>(_userData);
        _ = services.AddSingleton<ICoreDbContext>(_core);
        _services = services.BuildServiceProvider();
        _tokenUsage = new TokenUsageService(
            _services, new MemoryCache(new MemoryCacheOptions()), NullLogger<TokenUsageService>.Instance);
    }

    public void Dispose()
    {
        _services.Dispose();
        _userData.Dispose();
        _core.Dispose();
        GC.SuppressFinalize(this);
    }

    private static LlmUsageRecord Identity(string configuredModel)
    {
        return new LlmUsageRecord
        {
            OrganizationId = ORGANIZATION_ID,
            WorkflowExecutionId = "exec-1",
            BlockId = "block-1",
            UsageType = UsageType.LlmAgentRun,
            ExecutionContext = TokenExecutionContext.WorkflowExecution,
            Provider = AGENT_RUNTIME_PROVIDER,
            Family = "anthropic",
            ModelId = configuredModel,
            Components = [],
            Description = "Agent block 'Report'",
        };
    }

    private static AgentRunBill Bill(
        string configuredModel,
        decimal runtimeCostUsd,
        bool priceTokens = true,
        params AgentRunModelTokens[] models)
    {
        return new AgentRunBill(Identity(configuredModel), 1, runtimeCostUsd, models, configuredModel, 1, priceTokens);
    }

    private static AgentRunModelTokens Model(string model, params AgentRunCallTokens[] calls)
    {
        return new AgentRunModelTokens(model, calls);
    }

    private async Task<List<TokenUsage>> RowsAsync()
    {
        return await _userData.TokenUsages.Include(static row => row.Components).ToListAsync(TestContext.Current.CancellationToken);
    }

    private static TokenUsage SpendRow(List<TokenUsage> rows)
    {
        return Assert.Single(rows, static row => row.Components.Any(c => c.ComponentType == PricingComponentType.AgentRunSpend));
    }

    private static string MetadataField(TokenUsage row, string field)
    {
        using JsonDocument metadata = JsonDocument.Parse(row.Metadata!);

        return metadata.RootElement.GetProperty(field).GetString()!;
    }

    /// <summary>
    /// One million of each token kind, each kind in a request of its own and well under any
    /// long-prompt threshold, so the bill per kind is the list price per MTok at the us.
    /// profile's regional rate.
    /// </summary>
    [Theory]
    [InlineData("us.anthropic.claude-haiku-5-5", 0.10, 0.50, 0.125, 0.20, 0.01)]
    [InlineData("us.anthropic.claude-sonnet-5-5", 2.00, 10.00, 2.50, 4.00, 0.10)]
    [InlineData("us.anthropic.claude-sonnet-5", 2.00, 10.00, 2.50, 4.00, 0.20)]
    [InlineData("us.anthropic.claude-opus-5-5", 4.00, 20.00, 5.00, 8.00, 0.20)]
    public async Task EachAllowedModel_IsBilledAtItsRegionalPricePerTokenKind(
        string requestedModel, double input, double output, double cacheWrite5m, double cacheWrite1h, double cacheRead)
    {
        const long PART = ONE_MILLION / 20;
        List<AgentRunCallTokens> calls = [];
        for (int part = 0; part < 20; part++)
        {
            calls.Add(new AgentRunCallTokens(PART, 0, 0, 0, 0));
            calls.Add(new AgentRunCallTokens(0, PART, 0, 0, 0));
            calls.Add(new AgentRunCallTokens(0, 0, PART, 0, 0));
            calls.Add(new AgentRunCallTokens(0, 0, 0, PART, 0));
            calls.Add(new AgentRunCallTokens(0, 0, 0, 0, PART));
        }

        decimal recorded = await AgentRunBilling.RecordAsync(
            Bill(requestedModel, 99m, models: Model(requestedModel, [.. calls])), _tokenUsage, TestContext.Current.CancellationToken);

        decimal expected = (decimal)(input + output + cacheWrite5m + cacheWrite1h + cacheRead) * US_PROFILE;
        Assert.Equal(expected, recorded);
        List<TokenUsage> rows = await RowsAsync();
        TokenUsage tokens = Assert.Single(rows, static row => row.Components.All(c => c.ComponentType != PricingComponentType.AgentRunSpend));
        Assert.Equal((decimal)input * US_PROFILE, Assert.Single(tokens.Components, static c => c.ComponentType == PricingComponentType.Input).Cost);
        Assert.Equal((decimal)output * US_PROFILE, Assert.Single(tokens.Components, static c => c.ComponentType == PricingComponentType.Output).Cost);
        Assert.Equal((decimal)cacheWrite5m * US_PROFILE, Assert.Single(tokens.Components, static c => c.ComponentType == PricingComponentType.CacheWrite5m).Cost);
        Assert.Equal((decimal)cacheWrite1h * US_PROFILE, Assert.Single(tokens.Components, static c => c.ComponentType == PricingComponentType.CacheWrite1h).Cost);
        Assert.Equal((decimal)cacheRead * US_PROFILE, Assert.Single(tokens.Components, static c => c.ComponentType == PricingComponentType.CacheRead).Cost);
        Assert.Equal(0m, Assert.Single(SpendRow(rows).Components).Cost);
    }

    /// <summary>
    /// An audited block: 4 input, 72 output, 9,219 cache-write tokens on Haiku 5.5, for
    /// which the runtime reported $0.047551 (the Opus 5.5 price of those tokens).
    /// </summary>
    [Fact]
    public async Task AHaiku55Run_IsBilledAtHaiku55Rates_NotAtTheRuntimesFigure()
    {
        decimal recorded = await AgentRunBilling.RecordAsync(
            Bill(HAIKU_5_5_REQUESTED, 0.047551m, models: Model(HAIKU_5_5, new AgentRunCallTokens(4, 72, 9219, 0, 0))),
            _tokenUsage,
            TestContext.Current.CancellationToken);

        Assert.Equal(0.001188775m * US_PROFILE, recorded);
        TokenUsage spend = SpendRow(await RowsAsync());
        Assert.Equal(0m, Assert.Single(spend.Components).Cost);
        Assert.Equal(1, Assert.Single(spend.Components).Units);
        Assert.Equal(AgentTokenProvenance.COST_FROM_TOKENS, MetadataField(spend, COST_SOURCE_FIELD));
        Assert.Equal("0.047551", MetadataField(spend, RUNTIME_COST_FIELD));
    }

    [Fact]
    public async Task AHaiku55RequestOverTheThreshold_PaysTheLongPromptPriceOnEveryToken()
    {
        var longRequest = new AgentRunCallTokens(1_000, 10_000, 20_000, 0, 80_000);
        var shortRequest = new AgentRunCallTokens(1_000, 10_000, 20_000, 0, 79_000);

        decimal longCost = await AgentRunBilling.RecordAsync(
            Bill(HAIKU_5_5_REQUESTED, 1m, models: Model(HAIKU_5_5, longRequest)), _tokenUsage, TestContext.Current.CancellationToken);
        decimal shortCost = await AgentRunBilling.RecordAsync(
            Bill(HAIKU_5_5_REQUESTED, 1m, models: Model(HAIKU_5_5, shortRequest)), _tokenUsage, TestContext.Current.CancellationToken);

        // 101,000 prompt tokens: $0.50 in, $2.50 out, $0.625 5m write, $0.05 read per MTok.
        Assert.Equal((1_000m * 0.50m + 10_000m * 2.50m + 20_000m * 0.625m + 80_000m * 0.05m) / ONE_MILLION * US_PROFILE, longCost);
        // Exactly 100,000 prompt tokens is not over the threshold.
        Assert.Equal((1_000m * 0.10m + 10_000m * 0.50m + 20_000m * 0.125m + 79_000m * 0.01m) / ONE_MILLION * US_PROFILE, shortCost);
    }

    [Fact]
    public async Task OtherModels_HaveNoLongPromptPrice()
    {
        decimal recorded = await AgentRunBilling.RecordAsync(
            Bill("us.anthropic.claude-sonnet-5-5", 1m, models: Model(SONNET_5_5, new AgentRunCallTokens(0, 0, 0, 0, 900_000))),
            _tokenUsage,
            TestContext.Current.CancellationToken);

        Assert.Equal(0.09m * US_PROFILE, recorded);
    }

    [Fact]
    public async Task AWithheldTrace_BillsTheRuntimesFigure_AndSaysTheTokensAreUnknown()
    {
        decimal recorded = await AgentRunBilling.RecordAsync(Bill(HAIKU_5_5_REQUESTED, 0.08m), _tokenUsage, TestContext.Current.CancellationToken);

        Assert.Equal(0.08m, recorded);
        TokenUsage spend = Assert.Single(await RowsAsync());
        Assert.Equal(0.08m, Assert.Single(spend.Components).Cost);
        Assert.Equal(AgentTokenProvenance.UNKNOWN, MetadataField(spend, "tokenSource"));
        Assert.Equal(AgentTokenProvenance.COST_FROM_RUNTIME, MetadataField(spend, COST_SOURCE_FIELD));
    }

    [Fact]
    public async Task AModelWithNoPricingRow_LeavesTheRestOfTheRuntimesFigureOnTheSpendRow()
    {
        decimal recorded = await AgentRunBilling.RecordAsync(
            Bill(
                "us.anthropic.claude-sonnet-5",
                0.50m,
                models:
                [
                    Model(SONNET_5, new AgentRunCallTokens(0, 10_000, 0, 0, 0)),
                    Model("claude-unpriced-9", new AgentRunCallTokens(100, 100, 0, 0, 0)),
                ]),
            _tokenUsage,
            TestContext.Current.CancellationToken);

        // The runtime's figure is a list price, so the unpriced model keeps its list-price share
        // and the priced one is billed at the regional rate on top.
        Assert.Equal(0.40m + (0.10m * US_PROFILE), recorded);
        TokenUsage spend = SpendRow(await RowsAsync());
        Assert.Equal(0.40m, Assert.Single(spend.Components).Cost);
        Assert.Equal(AgentTokenProvenance.COST_FROM_RUNTIME, MetadataField(spend, COST_SOURCE_FIELD));
    }

    [Fact]
    public async Task TokensForCountingOnly_AreRecordedAtZero_AndTheRuntimesFigureIsTheBill()
    {
        decimal recorded = await AgentRunBilling.RecordAsync(
            Bill("us.anthropic.claude-opus-5-5", 0.02m, priceTokens: false, Model(OPUS_5_5, new AgentRunCallTokens(1_000, 1_000, 0, 0, 0))),
            _tokenUsage,
            TestContext.Current.CancellationToken);

        Assert.Equal(0.02m, recorded);
        List<TokenUsage> rows = await RowsAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(0.02m, Assert.Single(SpendRow(rows).Components).Cost);
        TokenUsage tokens = Assert.Single(rows, static row => row.Components.All(c => c.ComponentType != PricingComponentType.AgentRunSpend));
        Assert.All(tokens.Components, static component => Assert.Equal(0m, component.Cost));
    }

    [Fact]
    public async Task NoSpendAndNoTokens_RecordsNothing()
    {
        decimal recorded = await AgentRunBilling.RecordAsync(Bill(HAIKU_5_5_REQUESTED, 0m), _tokenUsage, TestContext.Current.CancellationToken);

        Assert.Equal(0m, recorded);
        Assert.Empty(await RowsAsync());
    }

    [Fact]
    public async Task TheTokenRows_KeepTheRunsIdentity_UnderTheModelTheyCalled()
    {
        _ = await AgentRunBilling.RecordAsync(
            Bill(
                HAIKU_5_5_REQUESTED,
                0.01m,
                models: Model("us.anthropic.claude-haiku-4-5-20251001-v1:0", new AgentRunCallTokens(1_052, 24, 0, 0, 0, Summed: true))),
            _tokenUsage,
            TestContext.Current.CancellationToken);

        TokenUsage tokens = Assert.Single(await RowsAsync(), static row => row.Components.All(c => c.ComponentType != PricingComponentType.AgentRunSpend));
        Assert.Equal("claude-haiku-4-5-20251001", tokens.ModelId);
        Assert.Equal(AGENT_RUNTIME_PROVIDER, tokens.Provider);
        Assert.Equal("block-1", tokens.BlockId);
        Assert.Equal("exec-1", tokens.WorkflowExecutionId);
        Assert.Equal(UsageType.LlmAgentRun, tokens.UsageType);
        Assert.Equal("Agent block 'Report' tokens", tokens.Description);
        Assert.Equal((1_052m * 1.00m + 24m * 5.00m) / ONE_MILLION * US_PROFILE, tokens.Components.Sum(static c => c.Cost));
    }

    [Fact]
    public async Task APassWhoseTraceWasNotCollected_IsBilledAtTheRuntimesFigure()
    {
        decimal recorded = await AgentRunBilling.RecordAsync(
            new AgentRunBill(
                Identity(HAIKU_5_5_REQUESTED),
                2,
                3.0012m,
                [Model(HAIKU_5_5, new AgentRunCallTokens(4, 72, 9219, 0, 0))],
                HAIKU_5_5_REQUESTED,
                1,
                UncoveredRuntimeCostUsd: 3m),
            _tokenUsage,
            TestContext.Current.CancellationToken);

        Assert.Equal(3m + (0.001188775m * US_PROFILE), recorded);
        TokenUsage spend = SpendRow(await RowsAsync());
        Assert.Equal(3m, Assert.Single(spend.Components).Cost);
        Assert.Equal(AgentTokenProvenance.COST_FROM_RUNTIME, MetadataField(spend, COST_SOURCE_FIELD));
    }

    [Fact]
    public void UncoveredCostUsd_CountsTheRunsThatHaveNoTracePack()
    {
        AgentRunAttempt collected = new(1, "run-1", AgentRunStatus.Failed, null, 2m, Unacknowledged: false);
        AgentRunAttempt lost = new(2, "run-2", AgentRunStatus.Succeeded, null, 3m, Unacknowledged: false);
        Domain.Entities.AgentRuntime.AgentRunTraceFile pack = new()
        {
            Path = AgentRunTraceTokens.TRACE_FILE_PATH,
            RunExecutionId = "run-1",
            Attempt = 1,
            Content = [],
        };

        Assert.Equal(3m, AgentRunTraceTokens.UncoveredCostUsd([collected, lost], [pack]));
        Assert.Equal(5m, AgentRunTraceTokens.UncoveredCostUsd([collected, lost], null));
    }

    /// <summary>
    /// A summed request may be several requests, each under the threshold, so it never pays the
    /// long-prompt price.
    /// </summary>
    [Fact]
    public async Task ASummedHaiku55RequestOverTheThreshold_KeepsTheStandardPrice()
    {
        decimal recorded = await AgentRunBilling.RecordAsync(
            Bill(HAIKU_5_5_REQUESTED, 1m, models: Model(HAIKU_5_5, new AgentRunCallTokens(0, 0, 0, 0, 120_000, Summed: true))),
            _tokenUsage,
            TestContext.Current.CancellationToken);

        Assert.Equal(120_000m * 0.01m / ONE_MILLION * US_PROFILE, recorded);
    }
}

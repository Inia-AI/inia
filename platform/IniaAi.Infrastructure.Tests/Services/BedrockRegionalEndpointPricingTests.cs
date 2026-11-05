using IniaAi.AiConnection.Common;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Configuration;
using IniaAi.Configuration.Enums.Anthropic;
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
using Microsoft.Extensions.Options;
using TokenExecutionContext = IniaAi.Domain.Entities.TokenUsage.Enums.ExecutionContext;

namespace IniaAi.Infrastructure.Tests.Services;

/// <summary>
/// Bedrock bills a regional inference profile (<c>us.</c>, <c>eu.</c>) at 1.1 times the global
/// price for Claude models from the 4.5 generation on. The seeded rows carry that
/// multiplier, and both the classic ledger and agent billing apply it from the endpoint a call
/// went through. The Anthropic API and Foundry rows have one price.
/// </summary>
public sealed class BedrockRegionalEndpointPricingTests : IDisposable
{
    private const string ORGANIZATION_ID = "org-regional";
    private const int INPUT_TOKENS = 1_000_000;
    private const int OUTPUT_TOKENS = 100_000;

    // Claude Opus 5.5 list price: $4 per million input, $20 per million output.
    private const decimal OPUS_5_5_LIST_COST = 4.00m + 2.00m;

    private readonly UserDataDbContext _userData;
    private readonly CoreDbContext _core;
    private readonly ServiceProvider _services;

    public BedrockRegionalEndpointPricingTests()
    {
        string databaseName = $"regional-{Guid.NewGuid()}";
        _userData = new UserDataDbContext(new DbContextOptionsBuilder<UserDataDbContext>()
            .UseInMemoryDatabase($"{databaseName}-userdata").Options);
        _core = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase($"{databaseName}-core").Options);
        _core.ModelPricings.AddRange(ModelPricingSeedingService.GetDefaultPricings());
        _ = _core.SaveChanges();

        var services = new ServiceCollection();
        _ = services.AddSingleton<IUserDataDbContext>(_userData);
        _ = services.AddSingleton<ICoreDbContext>(_core);
        _ = services.AddSingleton(Options.Create(new PlatformSettings
        {
            Anthropic = new() { AdvancedModel = AiModel.UsAwsSonnet5_5 },
        }));
        _services = services.BuildServiceProvider();
    }

    [Theory]
    [InlineData("us.anthropic.claude-opus-5-5", 1.10)]
    [InlineData("eu.anthropic.claude-opus-5-5", 1.10)]
    [InlineData("global.anthropic.claude-opus-5-5", 1.00)]
    public async Task ABedrockCall_IsPricedAtItsEndpointsRate(string endpointModelId, double multiple)
    {
        decimal recorded = await CreateService().RecordLlmUsageAsync(
            Record(ModelProviders.AWS_BEDROCK, endpointModelId), TestContext.Current.CancellationToken);

        Assert.Equal(OPUS_5_5_LIST_COST * (decimal)multiple, recorded);
    }

    [Fact]
    public async Task ABedrockCallWithNoKnownEndpoint_IsPricedAtTheStacksRoute()
    {
        decimal recorded = await CreateService().RecordLlmUsageAsync(
            Record(ModelProviders.AWS_BEDROCK, endpointModelId: "claude-opus-5-5"), TestContext.Current.CancellationToken);

        Assert.Equal(OPUS_5_5_LIST_COST * 1.10m, recorded);
    }

    [Theory]
    [InlineData(ModelProviders.ANTHROPIC_API)]
    [InlineData(ModelProviders.AZURE_FOUNDRY)]
    public async Task AnAnthropicApiOrFoundryCall_IsPricedAtList(string provider)
    {
        decimal recorded = await CreateService().RecordLlmUsageAsync(
            Record(provider, endpointModelId: "claude-opus-5-5"), TestContext.Current.CancellationToken);

        Assert.Equal(OPUS_5_5_LIST_COST, recorded);
    }

    [Fact]
    public void TheSeededRows_CarryTheMultiplier_OnlyOnBedrockRowsOfTheCurrentGenerations()
    {
        List<ModelPricing> rows = ModelPricingSeedingService.GetDefaultPricings();

        Assert.All(
            rows.Where(static row => row.Provider != ModelProviders.AWS_BEDROCK || row.PricingType != PricingType.Llm),
            static row => Assert.Null(row.RegionalEndpointMultiplier));
        Assert.Equal(1.10m, Single(rows, "claude-sonnet-5-5").RegionalEndpointMultiplier);
        Assert.Equal(1.10m, Single(rows, "claude-haiku-4-5-20251001").RegionalEndpointMultiplier);
        Assert.Equal(1.10m, Single(rows, "claude-sonnet-4-5-20250929").RegionalEndpointMultiplier);
        Assert.Null(Single(rows, "claude-sonnet-4-20250514").RegionalEndpointMultiplier);
        Assert.Null(Single(rows, "claude-opus-4-1-20250515").RegionalEndpointMultiplier);
        Assert.Null(Single(rows, "claude-3-7-sonnet-20250219").RegionalEndpointMultiplier);
    }

    [Fact]
    public async Task AnAgentRunOnAUsProfile_IsBilledAtTheRegionalRate()
    {
        var tokenUsage = CreateService();
        var bill = new AgentRunBill(
            Record(ModelProviders.AWS_BEDROCK, endpointModelId: null) with { UsageType = UsageType.LlmAgentRun },
            Runs: 1,
            RuntimeCostUsd: 0m,
            Models:
            [
                new AgentRunModelTokens(
                    "claude-opus-5-5",
                    [new AgentRunCallTokens(INPUT_TOKENS, OUTPUT_TOKENS, 0, 0, 0, Summed: false)]),
            ],
            ConfiguredModelId: "us.anthropic.claude-opus-5-5",
            Round: 1);

        decimal total = await AgentRunBilling.RecordAsync(bill, tokenUsage, TestContext.Current.CancellationToken);

        Assert.Equal(OPUS_5_5_LIST_COST * 1.10m, total);
    }

    public void Dispose()
    {
        _services.Dispose();
        _userData.Dispose();
        _core.Dispose();
    }

    private static ModelPricing Single(List<ModelPricing> rows, string modelId)
    {
        return rows.Single(row => row.Provider == ModelProviders.AWS_BEDROCK && row.ModelId == modelId);
    }

    private TokenUsageService CreateService()
    {
        return new TokenUsageService(_services, new MemoryCache(new MemoryCacheOptions()), NullLogger<TokenUsageService>.Instance);
    }

    private static LlmUsageRecord Record(string provider, string? endpointModelId)
    {
        return new LlmUsageRecord
        {
            OrganizationId = ORGANIZATION_ID,
            UsageType = UsageType.LlmBlockExecution,
            ExecutionContext = TokenExecutionContext.WorkflowExecution,
            Provider = provider,
            Family = "anthropic",
            ModelId = "claude-opus-5-5",
            Components = UsageComponentHelper.FromTokens(INPUT_TOKENS, OUTPUT_TOKENS),
            Description = "AI Query block 'Extract_Cash'",
            IsSingleRequest = true,
            EndpointModelId = endpointModelId,
        };
    }
}

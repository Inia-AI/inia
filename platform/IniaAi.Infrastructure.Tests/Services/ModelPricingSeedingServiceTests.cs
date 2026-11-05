using IniaAi.AiConnection.Common;
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
/// The pricing rows an Azure AI Foundry deployment bills against are keyed on the deployment
/// names the stack configures, and carry the same list prices as the Bedrock rows for the same
/// model — never a price of their own. Sonnet 5's rows carry its own $2/$10 list rate on every
/// provider: the seeder re-applies seeded prices on every start, so a row corrected
/// only in the database would be reverted at the next deploy. Opus 5.5 is the first Opus priced
/// below its predecessor, so its rows must never fall back to an older Opus's rates.
/// </summary>
public class ModelPricingSeedingServiceTests : IDisposable
{
    private const string SONNET_5 = "claude-sonnet-5";
    private const string SONNET_5_5 = "claude-sonnet-5-5";
    private const string OPUS_4_8 = "claude-opus-4-8";
    private const string OPUS_5 = "claude-opus-5";
    private const string OPUS_5_5 = "claude-opus-5-5";
    private const string SONNET_4_6 = "claude-sonnet-4-6";
    private const string HAIKU_4_5_DATED = "claude-haiku-4-5-20251001";
    private const string HAIKU_4_5 = "claude-haiku-4-5";
    private const string HAIKU_5_5 = "claude-haiku-5-5";
    private const string ORGANIZATION_ID = "org-1";
    private const int ONE_MILLION_TOKENS = 1_000_000;

    private static readonly string[] _foundryDeployedModels = [OPUS_4_8, OPUS_5, OPUS_5_5, SONNET_5, SONNET_5_5, SONNET_4_6, HAIKU_4_5_DATED, HAIKU_5_5];

    private readonly UserDataDbContext _userData;
    private readonly CoreDbContext _core;
    private readonly ServiceProvider _services;

    public ModelPricingSeedingServiceTests()
    {
        string databaseName = $"pricing-seed-{Guid.NewGuid()}";
        _userData = new UserDataDbContext(new DbContextOptionsBuilder<UserDataDbContext>()
            .UseInMemoryDatabase($"{databaseName}-userdata").Options);
        _core = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase($"{databaseName}-core").Options);

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

    private static List<ModelPricing> Defaults()
    {
        return ModelPricingSeedingService.GetDefaultPricings();
    }

    private static ModelPricing Row(IEnumerable<ModelPricing> rows, string provider, string modelId)
    {
        return Assert.Single(rows, row => row.Provider == provider && row.ModelId == modelId);
    }

    private static decimal Cost(ModelPricing row, PricingComponentType type)
    {
        return Assert.Single(row.Components, component => component.ComponentType == type).Cost;
    }

    [Theory]
    [InlineData(OPUS_4_8)]
    [InlineData(OPUS_5)]
    [InlineData(OPUS_5_5)]
    [InlineData(SONNET_5)]
    [InlineData(SONNET_5_5)]
    [InlineData(SONNET_4_6)]
    [InlineData(HAIKU_4_5_DATED)]
    [InlineData(HAIKU_5_5)]
    public void AzureFoundryRow_ExistsForEveryFoundryDeployedModel(string modelId)
    {
        ModelPricing row = Row(Defaults(), ModelProviders.AZURE_FOUNDRY, modelId);

        Assert.Equal(PricingType.Llm, row.PricingType);
        Assert.Equal("Anthropic", row.Family);
        Assert.Contains("Azure AI Foundry", row.DisplayName);
        Assert.True(row.IsActive);
    }

    [Fact]
    public void AzureFoundryRows_CarryTheBedrockRowsPricesUnchanged()
    {
        List<ModelPricing> defaults = Defaults();
        List<ModelPricing> foundryRows = [.. defaults.Where(static row => row.Provider == ModelProviders.AZURE_FOUNDRY)];

        Assert.Equal(_foundryDeployedModels.Length, foundryRows.Count);

        foreach (ModelPricing foundryRow in foundryRows)
        {
            ModelPricing bedrockRow = Row(defaults, ModelProviders.AWS_BEDROCK, foundryRow.ModelId);

            Assert.Equal(bedrockRow.ModelAlias, foundryRow.ModelAlias);
            Assert.Equal(
                bedrockRow.Components.Select(static c => (c.ComponentType, c.Cost, c.Unit, c.Currency)).OrderBy(static c => c.ComponentType),
                foundryRow.Components.Select(static c => (c.ComponentType, c.Cost, c.Unit, c.Currency)).OrderBy(static c => c.ComponentType));
        }
    }

    [Fact]
    public void AzureFoundryHaikuRow_AliasCoversTheUndatedDeploymentName()
    {
        ModelPricing row = Row(Defaults(), ModelProviders.AZURE_FOUNDRY, HAIKU_4_5_DATED);

        Assert.Equal(HAIKU_4_5, row.ModelAlias);
    }

    [Theory]
    [InlineData(ModelProviders.ANTHROPIC_API, SONNET_5, 2.00, 10.00, 2.50, 4.00, 0.20)]
    [InlineData(ModelProviders.AWS_BEDROCK, SONNET_5, 2.00, 10.00, 2.50, 4.00, 0.20)]
    [InlineData(ModelProviders.AZURE_FOUNDRY, SONNET_5, 2.00, 10.00, 2.50, 4.00, 0.20)]
    [InlineData(ModelProviders.ANTHROPIC_API, SONNET_5_5, 2.00, 10.00, 2.50, 4.00, 0.10)]
    [InlineData(ModelProviders.AWS_BEDROCK, SONNET_5_5, 2.00, 10.00, 2.50, 4.00, 0.10)]
    [InlineData(ModelProviders.AZURE_FOUNDRY, SONNET_5_5, 2.00, 10.00, 2.50, 4.00, 0.10)]
    [InlineData(ModelProviders.ANTHROPIC_API, HAIKU_5_5, 0.10, 0.50, 0.125, 0.20, 0.01)]
    [InlineData(ModelProviders.AWS_BEDROCK, HAIKU_5_5, 0.10, 0.50, 0.125, 0.20, 0.01)]
    [InlineData(ModelProviders.AZURE_FOUNDRY, HAIKU_5_5, 0.10, 0.50, 0.125, 0.20, 0.01)]
    [InlineData(ModelProviders.ANTHROPIC_API, OPUS_5, 5.00, 25.00, 6.25, 10.00, 0.50)]
    [InlineData(ModelProviders.AWS_BEDROCK, OPUS_5, 5.00, 25.00, 6.25, 10.00, 0.50)]
    [InlineData(ModelProviders.AZURE_FOUNDRY, OPUS_5, 5.00, 25.00, 6.25, 10.00, 0.50)]
    [InlineData(ModelProviders.ANTHROPIC_API, OPUS_5_5, 4.00, 20.00, 5.00, 8.00, 0.20)]
    [InlineData(ModelProviders.AWS_BEDROCK, OPUS_5_5, 4.00, 20.00, 5.00, 8.00, 0.20)]
    [InlineData(ModelProviders.AZURE_FOUNDRY, OPUS_5_5, 4.00, 20.00, 5.00, 8.00, 0.20)]
    public void Model_IsPricedAtItsOwnListRateOnEveryProvider(
        string provider, string modelId, double input, double output, double cacheWrite5m, double cacheWrite1h, double cacheRead)
    {
        ModelPricing row = Row(Defaults(), provider, modelId);

        Assert.Equal((decimal)input, Cost(row, PricingComponentType.Input));
        Assert.Equal((decimal)output, Cost(row, PricingComponentType.Output));
        Assert.Equal((decimal)cacheWrite5m, Cost(row, PricingComponentType.CacheWrite5m));
        Assert.Equal((decimal)cacheWrite1h, Cost(row, PricingComponentType.CacheWrite1h));
        Assert.Equal((decimal)cacheRead, Cost(row, PricingComponentType.CacheRead));
    }

    private async Task SeedAsync()
    {
        var service = new ModelPricingSeedingService(_services, NullLogger<ModelPricingSeedingService>.Instance);
        await service.StartAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(service.ExecuteTask);
        await service.ExecuteTask;
        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Seeding_CorrectsASonnet5RowLeftAtTheOldRate_AndAddsTheMissingFoundryRows()
    {
        ModelPricing stale = Row(Defaults(), ModelProviders.AWS_BEDROCK, SONNET_5);
        foreach (ModelPricingComponent component in stale.Components)
        {
            component.Cost *= 1.5m;
        }

        _ = await _core.ModelPricings.AddAsync(stale, TestContext.Current.CancellationToken);
        _ = await _core.SaveChangesAsync(TestContext.Current.CancellationToken);

        await SeedAsync();

        List<ModelPricing> rows = await _core.ModelPricings
            .Include(static row => row.Components)
            .ToListAsync(TestContext.Current.CancellationToken);
        ModelPricing corrected = Row(rows, ModelProviders.AWS_BEDROCK, SONNET_5);
        Assert.Equal(2.00m, Cost(corrected, PricingComponentType.Input));
        Assert.Equal(10.00m, Cost(corrected, PricingComponentType.Output));
        _ = Row(rows, ModelProviders.AZURE_FOUNDRY, SONNET_5);
        _ = Row(rows, ModelProviders.AZURE_FOUNDRY, HAIKU_4_5_DATED);
    }

    private TokenUsageService CreateTokenUsageService()
    {
        return new TokenUsageService(
            _services,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<TokenUsageService>.Instance);
    }

    private static LlmUsageRecord FoundryUsage(string modelId, PricingComponentType componentType)
    {
        return new LlmUsageRecord
        {
            OrganizationId = ORGANIZATION_ID,
            UsageType = UsageType.LlmBlockExecution,
            ExecutionContext = TokenExecutionContext.WorkflowExecution,
            Provider = ModelProviders.AZURE_FOUNDRY,
            Family = "anthropic",
            ModelId = modelId,
            Components = [new UsageComponent { ComponentType = componentType, Units = ONE_MILLION_TOKENS }],
            Description = "AI Query block 'Report'",
        };
    }

    [Theory]
    [InlineData(HAIKU_4_5, PricingComponentType.Input, 1.00)]
    [InlineData(SONNET_5, PricingComponentType.Output, 10.00)]
    [InlineData(OPUS_5_5, PricingComponentType.Output, 20.00)]
    [InlineData(SONNET_5_5, PricingComponentType.Output, 10.00)]
    [InlineData(HAIKU_5_5, PricingComponentType.Output, 0.50)]
    public async Task FoundryUsage_UnderADeploymentName_IsPricedAtThatModelsListRate(
        string deploymentName, PricingComponentType componentType, double expectedCost)
    {
        await SeedAsync();

        decimal cost = await CreateTokenUsageService().RecordLlmUsageAsync(
            FoundryUsage(deploymentName, componentType),
            TestContext.Current.CancellationToken);

        Assert.Equal((decimal)expectedCost, cost);
    }

    [Theory]
    [InlineData(ModelProviders.ANTHROPIC_API)]
    [InlineData(ModelProviders.AWS_BEDROCK)]
    [InlineData(ModelProviders.AZURE_FOUNDRY)]
    public void Haiku55Row_CarriesItsLongPromptPrices(string provider)
    {
        ModelPricing row = Row(Defaults(), provider, HAIKU_5_5);

        Assert.Equal(100_000, row.LongPromptThresholdTokens);
        Assert.Equal(0.50m, Cost(row, PricingComponentType.LongPromptInput));
        Assert.Equal(2.50m, Cost(row, PricingComponentType.LongPromptOutput));
        Assert.Equal(0.625m, Cost(row, PricingComponentType.LongPromptCacheWrite5m));
        Assert.Equal(1.00m, Cost(row, PricingComponentType.LongPromptCacheWrite1h));
        Assert.Equal(0.05m, Cost(row, PricingComponentType.LongPromptCacheRead));
    }

    [Fact]
    public void OnlyHaiku55_HasALongPromptPrice()
    {
        Assert.All(
            Defaults().Where(static row => row.ModelId != HAIKU_5_5),
            static row => Assert.Null(row.LongPromptThresholdTokens));
    }

    [Fact]
    public async Task Seeding_AddsTheLongPromptPricesToAHaiku55RowThatLacksThem()
    {
        ModelPricing stale = Row(Defaults(), ModelProviders.AWS_BEDROCK, HAIKU_5_5);
        stale.LongPromptThresholdTokens = null;
        stale.Components = [.. stale.Components.Where(static c => ModelPricingRates.LongPromptCounterpartOf(c.ComponentType) is not null)];

        _ = await _core.ModelPricings.AddAsync(stale, TestContext.Current.CancellationToken);
        _ = await _core.SaveChangesAsync(TestContext.Current.CancellationToken);

        await SeedAsync();

        ModelPricing corrected = Row(
            await _core.ModelPricings.Include(static row => row.Components).ToListAsync(TestContext.Current.CancellationToken),
            ModelProviders.AWS_BEDROCK,
            HAIKU_5_5);
        Assert.Equal(100_000, corrected.LongPromptThresholdTokens);
        Assert.Equal(0.50m, Cost(corrected, PricingComponentType.LongPromptInput));
        Assert.Equal(0.10m, Cost(corrected, PricingComponentType.Input));
    }

    private static LlmUsageRecord BedrockHaiku55Usage(bool singleRequest, int input, int output)
    {
        return new LlmUsageRecord
        {
            OrganizationId = ORGANIZATION_ID,
            UsageType = UsageType.LlmBlockExecution,
            ExecutionContext = TokenExecutionContext.WorkflowExecution,
            Provider = ModelProviders.AWS_BEDROCK,
            Family = "anthropic",
            ModelId = HAIKU_5_5,
            Components =
            [
                new UsageComponent { ComponentType = PricingComponentType.Input, Units = input },
                new UsageComponent { ComponentType = PricingComponentType.Output, Units = output },
            ],
            Description = "AI Query block 'Report'",
            IsSingleRequest = singleRequest,

            // The global profile pays the list price, so these figures read straight off the price list.
            EndpointModelId = "global.anthropic.claude-haiku-5-5",
        };
    }

    [Fact]
    public async Task OneHaiku55RequestOverTheThreshold_PaysTheLongPromptPrice()
    {
        await SeedAsync();

        decimal cost = await CreateTokenUsageService().RecordLlmUsageAsync(
            BedrockHaiku55Usage(singleRequest: true, input: 200_000, output: 1_000), TestContext.Current.CancellationToken);

        Assert.Equal((200_000m * 0.50m + 1_000m * 2.50m) / ONE_MILLION_TOKENS, cost);
    }

    /// <summary>
    /// An accumulator's total over the threshold may be several short requests, so it keeps the
    /// standard price.
    /// </summary>
    [Fact]
    public async Task ASummedHaiku55RecordOverTheThreshold_KeepsTheStandardPrice()
    {
        await SeedAsync();

        decimal cost = await CreateTokenUsageService().RecordLlmUsageAsync(
            BedrockHaiku55Usage(singleRequest: false, input: 200_000, output: 1_000), TestContext.Current.CancellationToken);

        Assert.Equal((200_000m * 0.10m + 1_000m * 0.50m) / ONE_MILLION_TOKENS, cost);
    }
}

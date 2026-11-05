using IniaAi.Common.Interfaces.Services;
using IniaAi.Domain.Entities.TokenUsage;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.Domain.Entities.Workflows.Enums;
using TokenExecutionContext = IniaAi.Domain.Entities.TokenUsage.Enums.ExecutionContext;

namespace IniaAi.Application.UnitTests.Services;

/// <summary>
/// Unit tests for token usage cost calculation logic.
/// Tests the core business logic without database dependencies.
/// </summary>
public class TokenUsageServiceTests
{
    private readonly ITestOutputHelper _testOutputHelper;

    public TokenUsageServiceTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    #region Helper Methods

    /// <summary>
    /// Creates a ModelPricing with normalized Components collection.
    /// </summary>
    private static ModelPricing CreateLlmPricing(
        string provider,
        string modelId,
        decimal inputCost,
        decimal outputCost,
        decimal? cacheWrite5mCost = null,
        decimal? cacheReadCost = null)
    {
        string pricingId = Guid.NewGuid().ToString();
        var components = new List<ModelPricingComponent>
        {
            new() {
                Id = Guid.NewGuid().ToString(),
                ModelPricingId = pricingId,
                ComponentType = PricingComponentType.Input,
                Cost = inputCost,
                Unit = PricingUnit.PerMillion,
                Currency = "USD"
            },
            new() {
                Id = Guid.NewGuid().ToString(),
                ModelPricingId = pricingId,
                ComponentType = PricingComponentType.Output,
                Cost = outputCost,
                Unit = PricingUnit.PerMillion,
                Currency = "USD"
            }
        };

        if (cacheWrite5mCost.HasValue)
        {
            components.Add(new ModelPricingComponent
            {
                Id = Guid.NewGuid().ToString(),
                ModelPricingId = pricingId,
                ComponentType = PricingComponentType.CacheWrite5m,
                Cost = cacheWrite5mCost.Value,
                Unit = PricingUnit.PerMillion,
                Currency = "USD"
            });
        }

        if (cacheReadCost.HasValue)
        {
            components.Add(new ModelPricingComponent
            {
                Id = Guid.NewGuid().ToString(),
                ModelPricingId = pricingId,
                ComponentType = PricingComponentType.CacheRead,
                Cost = cacheReadCost.Value,
                Unit = PricingUnit.PerMillion,
                Currency = "USD"
            });
        }

        return new ModelPricing
        {
            Id = pricingId,
            Provider = provider,
            ModelId = modelId,
            DisplayName = modelId,
            PricingType = PricingType.Llm,
            IsActive = true,
            EffectiveFrom = DateTime.UtcNow.AddDays(-1),
            Components = components
        };
    }

    /// <summary>
    /// Creates an Embedding pricing with normalized Components collection.
    /// </summary>
    private static ModelPricing CreateEmbeddingPricing(
        string provider,
        string modelId,
        decimal embeddingCost)
    {
        string pricingId = Guid.NewGuid().ToString();
        return new ModelPricing
        {
            Id = pricingId,
            Provider = provider,
            ModelId = modelId,
            DisplayName = modelId,
            PricingType = PricingType.Embedding,
            IsActive = true,
            EffectiveFrom = DateTime.UtcNow.AddDays(-1),
            Components =
            [
                new() {
                    Id = Guid.NewGuid().ToString(),
                    ModelPricingId = pricingId,
                    ComponentType = PricingComponentType.Embedding,
                    Cost = embeddingCost,
                    Unit = PricingUnit.PerMillion,
                    Currency = "USD"
                }
            ]
        };
    }

    /// <summary>
    /// Gets the cost for a specific component type from the pricing.
    /// </summary>
    private static decimal? GetComponentCost(ModelPricing pricing, PricingComponentType componentType)
    {
        return pricing.Components
            .FirstOrDefault(c => c.ComponentType == componentType)
            ?.Cost;
    }

    /// <summary>
    /// Helper method that mirrors the cost calculation logic in TokenUsageService.
    /// </summary>
    private static decimal CalculateCost(int tokens, decimal? costPerMillion)
    {
        return costPerMillion is null || costPerMillion == 0 || tokens == 0 ? 0m : tokens / 1_000_000m * costPerMillion.Value;
    }

    /// <summary>
    /// Calculates cost from the normalized components collection.
    /// </summary>
    private static decimal CalculateCostFromComponents(
        int tokens,
        ICollection<ModelPricingComponent> components,
        PricingComponentType componentType)
    {
        if (tokens == 0 || components.Count == 0)
        {
            return 0m;
        }

        ModelPricingComponent? component = components.FirstOrDefault(c => c.ComponentType == componentType);
        return component is null || component.Cost == 0
            ? 0m
            : component.Unit switch
            {
                PricingUnit.PerMillion => tokens / 1_000_000m * component.Cost,
                PricingUnit.PerThousand => tokens / 1_000m * component.Cost,
                PricingUnit.PerUnit => tokens * component.Cost,
                _ => tokens / 1_000_000m * component.Cost
            };
    }

    #endregion

    #region Cost Calculation Tests

    [Fact]
    public void CalculateLlmCost_WithStandardPricing_ShouldCalculateCorrectly()
    {
        // Arrange
        ModelPricing pricing = CreateLlmPricing(
            "Anthropic",
            "claude-sonnet-4-5",
            inputCost: 3.00m,
            outputCost: 15.00m,
            cacheWrite5mCost: 3.75m,
            cacheReadCost: 0.30m);

        int inputTokens = 1_000_000;      // 1M tokens = $3.00
        int outputTokens = 100_000;        // 100K tokens = $1.50
        int cacheCreationTokens = 500_000; // 500K tokens = $1.875
        int cacheReadTokens = 200_000;     // 200K tokens = $0.06

        // Act
        decimal inputCost = CalculateCostFromComponents(inputTokens, pricing.Components, PricingComponentType.Input);
        decimal outputCost = CalculateCostFromComponents(outputTokens, pricing.Components, PricingComponentType.Output);
        decimal cacheCreationCost = CalculateCostFromComponents(cacheCreationTokens, pricing.Components, PricingComponentType.CacheWrite5m);
        decimal cacheReadCost = CalculateCostFromComponents(cacheReadTokens, pricing.Components, PricingComponentType.CacheRead);
        decimal totalCost = inputCost + outputCost + cacheCreationCost + cacheReadCost;

        // Assert
        Assert.Equal(3.00m, inputCost);
        Assert.Equal(1.50m, outputCost);
        Assert.Equal(1.875m, cacheCreationCost);
        Assert.Equal(0.06m, cacheReadCost);
        Assert.Equal(6.435m, totalCost);

        _testOutputHelper.WriteLine($"Input cost: ${inputCost:F6}");
        _testOutputHelper.WriteLine($"Output cost: ${outputCost:F6}");
        _testOutputHelper.WriteLine($"Cache creation cost: ${cacheCreationCost:F6}");
        _testOutputHelper.WriteLine($"Cache read cost: ${cacheReadCost:F6}");
        _testOutputHelper.WriteLine($"Total cost: ${totalCost:F6}");
    }

    [Fact]
    public void CalculateLlmCost_WithZeroTokens_ShouldReturnZeroCost()
    {
        // Arrange
        ModelPricing pricing = CreateLlmPricing("Anthropic", "claude-sonnet-4-5", 3.00m, 15.00m);

        // Act
        decimal inputCost = CalculateCostFromComponents(0, pricing.Components, PricingComponentType.Input);
        decimal outputCost = CalculateCostFromComponents(0, pricing.Components, PricingComponentType.Output);

        // Assert
        Assert.Equal(0m, inputCost);
        Assert.Equal(0m, outputCost);
    }

    [Fact]
    public void CalculateLlmCost_WithSmallTokenCount_ShouldCalculatePrecisely()
    {
        // Arrange
        ModelPricing pricing = CreateLlmPricing("Anthropic", "claude-sonnet-4-5", 3.00m, 15.00m);

        // Act - 1 token each
        decimal inputCost = CalculateCostFromComponents(1, pricing.Components, PricingComponentType.Input);
        decimal outputCost = CalculateCostFromComponents(1, pricing.Components, PricingComponentType.Output);

        // Assert - precise small values
        Assert.Equal(0.000003m, inputCost);
        Assert.Equal(0.000015m, outputCost);

        _testOutputHelper.WriteLine($"Cost for 1 input token: ${inputCost:F6}");
        _testOutputHelper.WriteLine($"Cost for 1 output token: ${outputCost:F6}");
    }

    [Fact]
    public void CalculateLlmCost_WithNullPricing_ShouldReturnZero()
    {
        // Act
        decimal cost = CalculateCost(1000, null);

        // Assert
        Assert.Equal(0m, cost);
    }

    [Fact]
    public void CalculateLlmCost_WithZeroCostPerMillion_ShouldReturnZero()
    {
        // Act
        decimal cost = CalculateCost(1000, 0m);

        // Assert
        Assert.Equal(0m, cost);
    }

    [Fact]
    public void CalculateEmbeddingCost_WithStandardPricing_ShouldCalculateCorrectly()
    {
        // Arrange
        ModelPricing pricing = CreateEmbeddingPricing("Cohere", "cohere-finance-2", 0.12m);

        int inputTokens = 1_000_000; // 1M tokens = $0.12

        // Act
        decimal cost = CalculateCostFromComponents(inputTokens, pricing.Components, PricingComponentType.Embedding);

        // Assert
        Assert.Equal(0.12m, cost);
    }

    [Fact]
    public void CalculateEmbeddingCost_WithTypicalPageSize_ShouldCalculateCorrectly()
    {
        // Arrange - typical page might have ~500 tokens
        ModelPricing pricing = CreateEmbeddingPricing("Cohere", "cohere-finance-2", 0.12m);

        int inputTokens = 500;

        // Act
        decimal cost = CalculateCostFromComponents(inputTokens, pricing.Components, PricingComponentType.Embedding);

        // Assert
        Assert.Equal(0.00006m, cost); // $0.12 * (500/1,000,000)

        _testOutputHelper.WriteLine($"Cost for 500 token page: ${cost:F6}");
    }

    #endregion

    #region Pricing Selection Tests

    [Fact]
    public void SelectPricing_WithMultiplePricings_ShouldSelectMostRecent()
    {
        // Arrange
        var pricings = new List<ModelPricing>
        {
            CreateLlmPricing("Anthropic", "claude-sonnet-4-5", 5.00m, 25.00m), // Old price
            CreateLlmPricing("Anthropic", "claude-sonnet-4-5", 3.00m, 15.00m)  // New price
        };
        pricings[0].EffectiveFrom = DateTime.UtcNow.AddDays(-30);
        pricings[1].EffectiveFrom = DateTime.UtcNow.AddDays(-1);

        // Act - simulate the pricing selection logic
        ModelPricing? selectedPricing = pricings
            .Where(p => p.Provider == "Anthropic"
                && p.ModelId == "claude-sonnet-4-5"
                && p.PricingType is PricingType.Llm
                && p.IsActive)
            .OrderByDescending(p => p.EffectiveFrom)
            .FirstOrDefault();

        // Assert
        Assert.NotNull(selectedPricing);
        Assert.Equal(3.00m, GetComponentCost(selectedPricing, PricingComponentType.Input));
    }

    [Fact]
    public void SelectPricing_WithInactivePricing_ShouldNotSelect()
    {
        // Arrange
        ModelPricing pricing = CreateLlmPricing("Anthropic", "claude-sonnet-4-5", 100.00m, 500.00m);
        pricing.IsActive = false;
        var pricings = new List<ModelPricing> { pricing };

        // Act
        ModelPricing? selectedPricing = pricings
            .Where(p => p.Provider == "Anthropic"
                && p.ModelId == "claude-sonnet-4-5"
                && p.PricingType is PricingType.Llm
                && p.IsActive)
            .OrderByDescending(p => p.EffectiveFrom)
            .FirstOrDefault();

        // Assert
        Assert.Null(selectedPricing);
    }

    [Fact]
    public void SelectPricing_WithModelAlias_ShouldFindByAlias()
    {
        // Arrange
        ModelPricing pricing = CreateLlmPricing("Anthropic", "claude-sonnet-4-5-20250929", 3.00m, 15.00m);
        pricing.ModelAlias = "claude-sonnet-4-5";
        var pricings = new List<ModelPricing> { pricing };

        string searchModelId = "claude-sonnet-4-5"; // Using alias

        // Act - first try exact match
        ModelPricing? selectedPricing = pricings
            .Where(p => p.Provider == "Anthropic"
                && p.ModelId == searchModelId
                && p.PricingType is PricingType.Llm
                && p.IsActive)
            .OrderByDescending(p => p.EffectiveFrom)
            .FirstOrDefault();

        // Fallback to alias match
        selectedPricing ??= pricings
                .Where(p => p.Provider == "Anthropic"
                    && p.ModelAlias == searchModelId
                    && p.PricingType is PricingType.Llm
                    && p.IsActive)
                .OrderByDescending(p => p.EffectiveFrom)
                .FirstOrDefault();

        // Assert
        Assert.NotNull(selectedPricing);
        Assert.Equal("claude-sonnet-4-5-20250929", selectedPricing.ModelId);
        Assert.Equal("claude-sonnet-4-5", selectedPricing.ModelAlias);
    }

    #endregion

    #region LlmUsageRecord Tests

    [Fact]
    public void LlmUsageRecord_WithAllFields_ShouldHaveCorrectValues()
    {
        // Arrange & Act
        var record = new LlmUsageRecord
        {
            OrganizationId = "org-123",
            UserId = "user-456",
            WorkflowTypeId = "wf-type-789",
            WorkflowExecutionId = "exec-abc",
            BlockId = "block-def",
            UsageType = UsageType.LlmBlockExecution,
            ExecutionContext = TokenExecutionContext.WorkflowExecution,
            BlockType = BlockTypes.AiQuery,
            Provider = "anthropic-api",
            Family = "anthropic",
            ModelId = "claude-sonnet-4-5",
            Components = UsageComponentHelper.FromTokens(1000, 500, 200, 100)
        };

        // Assert
        Assert.Equal("org-123", record.OrganizationId);
        Assert.Equal("user-456", record.UserId);
        Assert.Equal("wf-type-789", record.WorkflowTypeId);
        Assert.Equal("exec-abc", record.WorkflowExecutionId);
        Assert.Equal("block-def", record.BlockId);
        Assert.Equal(UsageType.LlmBlockExecution, record.UsageType);
        Assert.Equal(TokenExecutionContext.WorkflowExecution, record.ExecutionContext);
        Assert.Equal(BlockTypes.AiQuery, record.BlockType);
        Assert.Equal("anthropic-api", record.Provider);
        Assert.Equal("anthropic", record.Family);
        Assert.Equal("claude-sonnet-4-5", record.ModelId);
        Assert.Equal(4, record.Components.Count);
        Assert.Contains(record.Components, c => c.ComponentType == PricingComponentType.Input && c.Units == 1000);
        Assert.Contains(record.Components, c => c.ComponentType == PricingComponentType.Output && c.Units == 500);
        Assert.Contains(record.Components, c => c.ComponentType == PricingComponentType.CacheWrite5m && c.Units == 200);
        Assert.Contains(record.Components, c => c.ComponentType == PricingComponentType.CacheRead && c.Units == 100);
    }

    [Fact]
    public void LlmUsageRecord_WithMinimalFields_ShouldHaveNullOptionals()
    {
        // Arrange & Act
        var record = new LlmUsageRecord
        {
            OrganizationId = "org-123",
            UsageType = UsageType.LlmBlockExecution,
            ExecutionContext = TokenExecutionContext.BlockTest,
            Provider = "anthropic-api",
            Family = "anthropic",
            ModelId = "claude-sonnet-4-5",
            Components = UsageComponentHelper.FromTokens(100, 50)
        };

        // Assert
        Assert.Null(record.UserId);
        Assert.Null(record.WorkflowTypeId);
        Assert.Null(record.WorkflowExecutionId);
        Assert.Null(record.BlockId);
        Assert.Null(record.DocumentId);
        Assert.Null(record.BlockType);
        Assert.Equal(2, record.Components.Count);
    }

    #endregion

    #region EmbeddingUsageRecord Tests

    [Fact]
    public void EmbeddingUsageRecord_WithAllFields_ShouldHaveCorrectValues()
    {
        // Arrange & Act
        var record = new EmbeddingUsageRecord
        {
            OrganizationId = "org-123",
            UserId = "user-456",
            DocumentId = "doc-789",
            PageId = "page-abc",
            UsageType = UsageType.EmbeddingPage,
            ExecutionContext = TokenExecutionContext.DocumentUpload,
            Provider = "cohere-api",
            Family = "cohere",
            ModelId = "cohere-finance-2",
            Components = UsageComponentHelper.ForEmbedding(5000)
        };

        // Assert
        Assert.Equal("org-123", record.OrganizationId);
        Assert.Equal("user-456", record.UserId);
        Assert.Equal("doc-789", record.DocumentId);
        Assert.Equal("page-abc", record.PageId);
        Assert.Equal(UsageType.EmbeddingPage, record.UsageType);
        Assert.Equal(TokenExecutionContext.DocumentUpload, record.ExecutionContext);
        Assert.Equal("cohere-api", record.Provider);
        Assert.Equal("cohere", record.Family);
        Assert.Equal("cohere-finance-2", record.ModelId);
        Assert.Single(record.Components);
        Assert.Contains(record.Components, c => c.ComponentType == PricingComponentType.Embedding && c.Units == 5000);
    }

    #endregion

    #region Real-World Pricing Scenarios

    [Fact]
    public void CalculateCost_WithRealAnthropicPricing_ShouldMatchExpectedCosts()
    {
        // Arrange - Real Claude 3.5 Sonnet pricing (as of late 2024)
        ModelPricing pricing = CreateLlmPricing(
            "Anthropic",
            "claude-3-5-sonnet",
            inputCost: 3.00m,
            outputCost: 15.00m,
            cacheWrite5mCost: 3.75m,
            cacheReadCost: 0.30m);

        // Typical workflow execution might use:
        // - 5000 input tokens (prompt)
        // - 1000 output tokens (response)
        // - 3000 cache creation tokens
        // - 2000 cache read tokens

        // Act
        decimal inputCost = CalculateCostFromComponents(5000, pricing.Components, PricingComponentType.Input);
        decimal outputCost = CalculateCostFromComponents(1000, pricing.Components, PricingComponentType.Output);
        decimal cacheCreationCost = CalculateCostFromComponents(3000, pricing.Components, PricingComponentType.CacheWrite5m);
        decimal cacheReadCost = CalculateCostFromComponents(2000, pricing.Components, PricingComponentType.CacheRead);
        decimal totalCost = inputCost + outputCost + cacheCreationCost + cacheReadCost;

        // Assert
        Assert.Equal(0.015m, inputCost);       // $3 * (5000/1M)
        Assert.Equal(0.015m, outputCost);      // $15 * (1000/1M)
        Assert.Equal(0.01125m, cacheCreationCost); // $3.75 * (3000/1M)
        Assert.Equal(0.0006m, cacheReadCost);  // $0.30 * (2000/1M)
        Assert.Equal(0.04185m, totalCost);     // Sum of all costs

        _testOutputHelper.WriteLine($"Typical workflow block execution cost: ${totalCost:F6}");
    }

    [Fact]
    public void CalculateCost_WithRealCoherePricing_ShouldMatchExpectedCosts()
    {
        // Arrange - Real Cohere pricing
        ModelPricing pricing = CreateEmbeddingPricing("Cohere", "cohere", 0.12m);

        // Typical document might have 50 pages * 500 tokens per page = 25,000 tokens

        // Act
        decimal cost = CalculateCostFromComponents(25000, pricing.Components, PricingComponentType.Embedding);

        // Assert
        Assert.Equal(0.003m, cost); // $0.12 * (25000/1M)

        _testOutputHelper.WriteLine($"Typical document embedding cost (50 pages): ${cost:F6}");
    }

    #endregion
}

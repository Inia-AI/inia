using IniaAi.Application.Common.Services;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Interfaces.Services;
using IniaAi.AiConnection.Common;
using IniaAi.Common.Extensions;
using IniaAi.Common.Text;
using IniaAi.Configuration;
using Microsoft.Extensions.Options;
using IniaAi.Domain.Entities.TokenUsage;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// Service for recording token usage and calculating costs based on model pricing.
/// Uses a fire-and-forget pattern to avoid impacting workflow execution performance.
/// </summary>
public class TokenUsageService : ITokenUsageService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TokenUsageService> _logger;

    /// <summary>
    /// The marker of the warning an embedding usage record logs when no price row matches its
    /// provider and model. An alarm reads it.
    /// </summary>
    public const string UNPRICED_EMBEDDING_LOG_MARKER = "EmbeddingUsageUnpriced";

    private const string PRICING_CACHE_KEY_PREFIX = "ModelPricing_";
    private static readonly TimeSpan PricingCacheDuration = TimeSpan.FromMinutes(30);

    public TokenUsageService(
        IServiceProvider serviceProvider,
        IMemoryCache cache,
        ILogger<TokenUsageService> logger)
    {
        _serviceProvider = serviceProvider;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<decimal> RecordLlmUsageAsync(
        LlmUsageRecord record,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using IServiceScope scope = _serviceProvider.CreateScope();
            IUserDataDbContext userDataDbContext = scope.ServiceProvider.GetRequiredService<IUserDataDbContext>();
            ICoreDbContext coreDbContext = scope.ServiceProvider.GetRequiredService<ICoreDbContext>();

            ModelPricing? pricing = null;
            if (record.Components.Count == 0
                || record.Components.Any(static component => component.CostUsd is null))
            {
                pricing = await GetPricingAsync(
                    coreDbContext,
                    record.Provider,
                    record.ModelId,
                    PricingType.Llm,
                    cancellationToken);

                if (pricing is null)
                {
                    _logger.LogWarning(
                        "No pricing found for provider {Provider}, model {ModelId}. Recording usage without cost calculation",
                        record.Provider,
                        record.ModelId);
                }
            }

            var tokenUsage = new TokenUsage
            {
                // Dimensional keys
                OrganizationId = record.OrganizationId,
                UserId = record.UserId,
                WorkflowTypeId = record.WorkflowTypeId,
                WorkflowExecutionId = record.WorkflowExecutionId,
                SectionId = record.SectionId,
                BlockId = record.BlockId,
                DocumentId = record.DocumentId,
                PageId = record.PageId,

                // Type classifications
                UsageType = record.UsageType,
                ExecutionContext = record.ExecutionContext,
                BlockType = record.BlockType,

                // Provider/Model
                Provider = record.Provider,
                Family = record.Family,
                ModelId = record.ModelId,

                // Reference to pricing used
                ModelPricingId = pricing?.Id,

                // Description and Metadata
                Description = record.Description,
                Metadata = record.Metadata,

                // Timestamp
                CreatedAt = DateTime.UtcNow
            };

            // Build components and calculate costs
            decimal endpointMultiplier = ModelPricingRates.EndpointMultiplierFor(
                pricing,
                ModelIdNormalizer.IsBedrockFormat(record.EndpointModelId)
                    ? record.EndpointModelId
                    : ConfiguredEndpointModelId(scope.ServiceProvider));
            BuildAndAddComponents(tokenUsage, record.Components, pricing, record.IsSingleRequest, endpointMultiplier);

            _ = await userDataDbContext.TokenUsages.AddAsync(tokenUsage, cancellationToken);
            _ = await userDataDbContext.SaveChangesAsync(cancellationToken);

            decimal totalCost = tokenUsage.Components.Sum(c => c.Cost);

            _logger.LogDebug(
                "Recorded LLM token usage: {ComponentCount} components, Total cost: ${TotalCost:F6}. Description: {Description}",
                tokenUsage.Components.Count,
                totalCost,
                LogSanitizer.ForLog(record.Description ?? "(none)"));

            return totalCost;
        }
        catch (Exception ex)
        {
            // Log but don't throw - token tracking should not break workflow execution
            _logger.LogError(ex,
                "Failed to record LLM token usage for org {OrganizationId}, model {ModelId}, workflow type {WorkflowTypeId}, execution {WorkflowExecutionId}: {Error}",
                record.OrganizationId,
                record.ModelId,
                LogSanitizer.ForLog(record.WorkflowTypeId),
                LogSanitizer.ForLog(record.WorkflowExecutionId),
                ex.Message);
            RecordFailure(record.UsageType, record.ModelId, ex);

            return 0m;
        }
    }

    /// <inheritdoc/>
    public async Task<ModelPricing?> FindLlmPricingAsync(
        string provider,
        string modelId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using IServiceScope scope = _serviceProvider.CreateScope();
            ICoreDbContext coreDbContext = scope.ServiceProvider.GetRequiredService<ICoreDbContext>();

            return await GetPricingAsync(coreDbContext, provider, modelId, PricingType.Llm, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Looking up pricing for provider {Provider}, model {ModelId} failed", provider, modelId);

            return null;
        }
    }

    /// <inheritdoc/>
    public async Task RecordEmbeddingUsageAsync(
        EmbeddingUsageRecord record,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using IServiceScope scope = _serviceProvider.CreateScope();
            IUserDataDbContext userDataDbContext = scope.ServiceProvider.GetRequiredService<IUserDataDbContext>();
            ICoreDbContext coreDbContext = scope.ServiceProvider.GetRequiredService<ICoreDbContext>();

            // Look up pricing from Core database (includes Components)
            ModelPricing? pricing = await GetPricingAsync(
                coreDbContext,
                record.Provider,
                record.ModelId,
                PricingType.Embedding,
                cancellationToken);

            if (pricing is null)
            {
                _logger.LogWarning(
                    UNPRICED_EMBEDDING_LOG_MARKER + ": No pricing found for embedding provider {Provider}, model {ModelId}. Recording usage without cost calculation",
                    record.Provider,
                    record.ModelId);
            }

            var tokenUsage = new TokenUsage
            {
                // Dimensional keys
                OrganizationId = record.OrganizationId,
                UserId = record.UserId,
                WorkflowTypeId = record.WorkflowTypeId,
                WorkflowExecutionId = record.WorkflowExecutionId,
                SectionId = record.SectionId,
                BlockId = record.BlockId,
                DocumentId = record.DocumentId,
                PageId = record.PageId,

                // Type classifications
                UsageType = record.UsageType,
                ExecutionContext = record.ExecutionContext,
                BlockType = record.BlockType,

                // Provider/Model
                Provider = record.Provider,
                Family = record.Family,
                ModelId = record.ModelId,

                // Reference to pricing used
                ModelPricingId = pricing?.Id,

                // Description and Metadata
                Description = record.Description,
                Metadata = record.Metadata,

                // Timestamp
                CreatedAt = DateTime.UtcNow
            };

            // Build components and calculate costs
            BuildAndAddComponents(tokenUsage, record.Components, pricing, singleRequest: false, endpointMultiplier: 1m);

            _ = await userDataDbContext.TokenUsages.AddAsync(tokenUsage, cancellationToken);
            _ = await userDataDbContext.SaveChangesAsync(cancellationToken);

            _logger.LogDebug(
                "Recorded embedding token usage: {ComponentCount} components. Total cost: ${TotalCost:F6}. Description: {Description}",
                tokenUsage.Components.Count,
                tokenUsage.Components.Sum(c => c.Cost),
                record.Description ?? "(none)");
        }
        catch (Exception ex)
        {
            // Log but don't throw - token tracking should not break document processing
            _logger.LogError(ex,
                "Failed to record embedding token usage for org {OrganizationId}, model {ModelId}, workflow type {WorkflowTypeId}, execution {WorkflowExecutionId}: {Error}",
                record.OrganizationId,
                record.ModelId,
                LogSanitizer.ForLog(record.WorkflowTypeId),
                LogSanitizer.ForLog(record.WorkflowExecutionId),
                ex.Message);
            RecordFailure(record.UsageType, record.ModelId, ex);
        }
    }

    /// <summary>
    /// The readiness report is anonymous, so it carries the usage type, the model and the kind of
    /// failure only; the workflow type, the execution and the database error are in the log line.
    /// </summary>
    private static void RecordFailure(UsageType usageType, string modelId, Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            return;
        }

        TokenUsageRecordingFailures.Instance.Record(
            $"{usageType} usage of model {modelId}: {(exception.InnerException ?? exception).GetType().Name}");
    }

    /// <summary>
    /// The route a record with no endpoint of its own was sent through: the stack's configured
    /// advanced model, since a stack calls every model through its default model's route.
    /// </summary>
    private static string? ConfiguredEndpointModelId(IServiceProvider services)
    {
        return services.GetService<IOptions<PlatformSettings>>()?.Value.Anthropic.AdvancedModel.GetEnumMember();
    }

    private async Task<ModelPricing?> GetPricingAsync(
        ICoreDbContext coreDbContext,
        string provider,
        string modelId,
        PricingType pricingType,
        CancellationToken cancellationToken)
    {
        string cacheKey = $"{PRICING_CACHE_KEY_PREFIX}{provider}_{modelId}_{pricingType}";

        if (_cache.TryGetValue(cacheKey, out ModelPricing? cachedPricing))
        {
            return cachedPricing;
        }

        // Try exact model ID match first (include Components for cost calculation)
        ModelPricing? pricing = await coreDbContext.ModelPricings
            .Include(p => p.Components)
            .Where(p => p.Provider == provider
                && p.ModelId == modelId
                && p.PricingType == pricingType
                && p.IsActive)
            .OrderByDescending(p => p.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);

        // If not found, try alias match
        pricing ??= await coreDbContext.ModelPricings
                .Include(p => p.Components)
                .Where(p => p.Provider == provider
                    && p.ModelAlias == modelId
                    && p.PricingType == pricingType
                    && p.IsActive)
                .OrderByDescending(p => p.EffectiveFrom)
                .FirstOrDefaultAsync(cancellationToken);

        // Cache the result (even if null, to avoid repeated lookups)
        _ = _cache.Set(cacheKey, pricing, PricingCacheDuration);

        return pricing;
    }

    /// <summary>
    /// Builds TokenUsageComponent records from Components and calculates costs. Only a record of
    /// one model request can pay a long-prompt price, read from its own prompt components.
    /// </summary>
    private static void BuildAndAddComponents(
        TokenUsage tokenUsage,
        IReadOnlyList<UsageComponent> recordComponents,
        ModelPricing? pricing,
        bool singleRequest,
        decimal endpointMultiplier)
    {
        long promptTokens = recordComponents
            .Where(static component => ModelPricingRates.PromptComponents.Contains(component.ComponentType))
            .Sum(static component => (long)Math.Max(0, component.Units));
        bool longPrompt = singleRequest && ModelPricingRates.IsLongPrompt(pricing, promptTokens);

        foreach (UsageComponent comp in recordComponents)
        {
            if (comp.Units <= 0)
            {
                continue;
            }

            decimal cost = comp.CostUsd
                ?? ModelPricingRates.CostOf(pricing, comp.ComponentType, comp.Units, longPrompt) * endpointMultiplier;

            tokenUsage.Components.Add(new TokenUsageComponent
            {
                ComponentType = comp.ComponentType,
                Units = comp.Units,
                Cost = cost,
                CreatedAt = DateTime.UtcNow
            });
        }
    }
}

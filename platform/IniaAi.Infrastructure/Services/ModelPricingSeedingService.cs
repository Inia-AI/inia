using IniaAi.AiConnection.Common;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Domain.Entities.TokenUsage;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// Background service that seeds default ModelPricing records on application startup.
/// Seeds Anthropic, and embedding provider pricing if the table is empty.
/// Uses normalized structure with ModelPricingComponent for flexible pricing.
/// </summary>
public class ModelPricingSeedingService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ModelPricingSeedingService> _logger;

    public ModelPricingSeedingService(
        IServiceProvider serviceProvider,
        ILogger<ModelPricingSeedingService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ModelPricing seeding service starting...");

        try
        {
            using IServiceScope scope = _serviceProvider.CreateScope();
            ICoreDbContext coreDbContext = scope.ServiceProvider.GetRequiredService<ICoreDbContext>();

            List<ModelPricing> defaultPricings = GetDefaultPricings();

            // Check if pricing already exists
            List<ModelPricing> existingRecords = await coreDbContext.ModelPricings
                .Include(p => p.Components)
                .ToListAsync(stoppingToken);

            if (existingRecords.Count == 0)
            {
                // Seed fresh data
                _logger.LogInformation("Seeding ModelPricing table with default pricing rates...");
                await coreDbContext.ModelPricings.AddRangeAsync(defaultPricings, stoppingToken);
                _ = await coreDbContext.SaveChangesAsync(stoppingToken);
                _logger.LogInformation("ModelPricing seeding completed. Seeded {Count} pricing records", defaultPricings.Count);
            }
            else
            {
                // Update existing records with correct pricing values
                _logger.LogInformation("ModelPricing table has {Count} records, updating with latest pricing...", existingRecords.Count);

                int updatedCount = 0;
                int addedCount = 0;

                foreach (ModelPricing defaultPricing in defaultPricings)
                {
                    // Find existing record by Provider + ModelId
                    ModelPricing? existing = existingRecords.FirstOrDefault(e =>
                        e.Provider == defaultPricing.Provider &&
                        e.ModelId == defaultPricing.ModelId);

                    if (existing is not null)
                    {
                        // Update model metadata if changed
                        bool hasChanges = false;

                        if (existing.ModelAlias != defaultPricing.ModelAlias)
                        {
                            existing.ModelAlias = defaultPricing.ModelAlias;
                            hasChanges = true;
                        }

                        if (existing.DisplayName != defaultPricing.DisplayName)
                        {
                            existing.DisplayName = defaultPricing.DisplayName;
                            hasChanges = true;
                        }

                        if (existing.Family != defaultPricing.Family)
                        {
                            existing.Family = defaultPricing.Family;
                            hasChanges = true;
                        }

                        if (existing.EffectiveFrom != defaultPricing.EffectiveFrom)
                        {
                            existing.EffectiveFrom = defaultPricing.EffectiveFrom;
                            hasChanges = true;
                        }

                        if (existing.LongPromptThresholdTokens != defaultPricing.LongPromptThresholdTokens)
                        {
                            existing.LongPromptThresholdTokens = defaultPricing.LongPromptThresholdTokens;
                            hasChanges = true;
                        }

                        if (existing.RegionalEndpointMultiplier != defaultPricing.RegionalEndpointMultiplier)
                        {
                            existing.RegionalEndpointMultiplier = defaultPricing.RegionalEndpointMultiplier;
                            hasChanges = true;
                        }

                        // Update components
                        foreach (ModelPricingComponent defaultComponent in defaultPricing.Components)
                        {
                            ModelPricingComponent? existingComponent = existing.Components
                                .FirstOrDefault(c => c.ComponentType == defaultComponent.ComponentType);

                            if (existingComponent is not null)
                            {
                                if (existingComponent.Cost != defaultComponent.Cost ||
                                    existingComponent.Unit != defaultComponent.Unit ||
                                    existingComponent.Currency != defaultComponent.Currency)
                                {
                                    existingComponent.Cost = defaultComponent.Cost;
                                    existingComponent.Unit = defaultComponent.Unit;
                                    existingComponent.Currency = defaultComponent.Currency;
                                    hasChanges = true;
                                }
                            }
                            else
                            {
                                // Add new component
                                existing.Components.Add(new ModelPricingComponent
                                {
                                    Id = Guid.NewGuid().ToString(),
                                    ModelPricingId = existing.Id,
                                    ComponentType = defaultComponent.ComponentType,
                                    Cost = defaultComponent.Cost,
                                    Unit = defaultComponent.Unit,
                                    Currency = defaultComponent.Currency
                                });
                                hasChanges = true;
                            }
                        }

                        if (hasChanges)
                        {
                            _ = coreDbContext.ModelPricings.Update(existing);
                            updatedCount++;
                        }
                    }
                    else
                    {
                        // Add new pricing record
                        _ = await coreDbContext.ModelPricings.AddAsync(defaultPricing, stoppingToken);
                        addedCount++;
                    }
                }

                if (updatedCount > 0 || addedCount > 0)
                {
                    _ = await coreDbContext.SaveChangesAsync(stoppingToken);
                    _logger.LogInformation("ModelPricing update completed. Updated {UpdatedCount}, Added {AddedCount} records",
                        updatedCount, addedCount);
                }
                else
                {
                    _logger.LogInformation("ModelPricing data is already up to date");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("ModelPricing seeding was cancelled during startup");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during ModelPricing seeding: {ErrorMessage}", ex.Message);
            // Don't rethrow - seeding failure shouldn't crash the application
        }
    }

    /// <summary>
    /// A model's prices for a request whose prompt is over <paramref name="ThresholdTokens"/>.
    /// </summary>
    private sealed record LongPromptTier(
        int ThresholdTokens, decimal Input, decimal Output, decimal Cache5m, decimal Cache1h, decimal CacheRead);

    /// <summary>
    /// Bedrock bills a regional endpoint (a geo profile such as <c>us.</c> or <c>eu.</c>, or an
    /// in-region call) at 1.1 times its global endpoint, for every token kind, for Claude models
    /// from the 4.5 generation on. Older models have one price. Source: the AWS Price List API
    /// (AmazonBedrockFoundationModels, us-east-1, "Standard" against "Standard, Global" and
    /// "Regional" against "Global" usage types), read 2026-10-16; the Bedrock pricing page
    /// https://aws.amazon.com/bedrock/pricing/ says the same.
    /// </summary>
    private const decimal BEDROCK_REGIONAL_ENDPOINT_MULTIPLIER = 1.10m;

    /// <summary>
    /// Claude Haiku 5.5 over 100,000 prompt tokens, the same on the Claude API, Bedrock (global
    /// endpoint) and Foundry: https://platform.claude.com/docs/en/about-claude/pricing (Long
    /// context pricing) and the AWS price list (AmazonBedrockFoundationModels, "Long Context,
    /// Global"), both read 2026-10-16.
    /// </summary>
    private static readonly LongPromptTier Haiku55LongPrompt = new(
        ThresholdTokens: 100_000, Input: 0.50m, Output: 2.50m, Cache5m: 0.625m, Cache1h: 1.00m, CacheRead: 0.05m);

    internal static List<ModelPricing> GetDefaultPricings()
    {
        var effectiveFrom = new DateTime(2024, 12, 1, 0, 0, 0, DateTimeKind.Utc);

        return
        [
            // ═══════════════════════════════════════════════════════════════
            // ANTHROPIC DIRECT API - LLM MODELS
            // https://platform.claude.com/docs/en/about-claude/pricing
            // ═══════════════════════════════════════════════════════════════

            CreateLlmPricing(ModelProviders.ANTHROPIC_API, "claude-fable-5", "claude-fable-5",
                "Claude Fable 5 (Direct API)", "Anthropic", effectiveFrom,
                input: 10.00m, output: 50.00m, cache5m: 12.50m, cache1h: 20.00m, cacheRead: 1.00m),

            // Opus 5.5 is the first Opus priced below its predecessor, and its cache reads bill at
            // 0.05x input rather than the usual 0.1x.
            CreateLlmPricing(ModelProviders.ANTHROPIC_API, "claude-opus-5-5", "claude-opus-5-5",
                "Claude Opus 5.5 (Direct API)", "Anthropic", effectiveFrom,
                input: 4.00m, output: 20.00m, cache5m: 5.00m, cache1h: 8.00m, cacheRead: 0.20m),

            CreateLlmPricing(ModelProviders.ANTHROPIC_API, "claude-opus-5", "claude-opus-5",
                "Claude Opus 5 (Direct API)", "Anthropic", effectiveFrom,
                input: 5.00m, output: 25.00m, cache5m: 6.25m, cache1h: 10.00m, cacheRead: 0.50m),

            CreateLlmPricing(ModelProviders.ANTHROPIC_API, "claude-opus-4-8", "claude-opus-4-8",
                "Claude Opus 4.8 (Direct API)", "Anthropic", effectiveFrom,
                input: 5.00m, output: 25.00m, cache5m: 6.25m, cache1h: 10.00m, cacheRead: 0.50m),

            // Sonnet 5.5 cache reads bill at 0.05x input ($0.10) since 2026-10-07; they billed at
            // $0.20 before that date (Bedrock pricing page note, read 2026-10-16).
            CreateLlmPricing(ModelProviders.ANTHROPIC_API, "claude-sonnet-5-5", "claude-sonnet-5-5",
                "Claude Sonnet 5.5 (Direct API)", "Anthropic", effectiveFrom,
                input: 2.00m, output: 10.00m, cache5m: 2.50m, cache1h: 4.00m, cacheRead: 0.10m),

            // Sonnet 5's $2/$10 launch price is its standard list price; the $3/$15 step-up was
            // withdrawn. A row corrected only in the database is re-seeded from here.
            CreateLlmPricing(ModelProviders.ANTHROPIC_API, "claude-sonnet-5", "claude-sonnet-5",
                "Claude Sonnet 5 (Direct API)", "Anthropic", effectiveFrom,
                input: 2.00m, output: 10.00m, cache5m: 2.50m, cache1h: 4.00m, cacheRead: 0.20m),

            CreateLlmPricing(ModelProviders.ANTHROPIC_API, "claude-opus-4-6", "claude-opus-4-6",
                "Claude Opus 4.6 (Direct API)", "Anthropic", effectiveFrom,
                input: 5.00m, output: 25.00m, cache5m: 6.25m, cache1h: 10.00m, cacheRead: 0.50m),

            CreateLlmPricing(ModelProviders.ANTHROPIC_API, "claude-sonnet-4-6", "claude-sonnet-4-6",
                "Claude Sonnet 4.6 (Direct API)", "Anthropic", effectiveFrom,
                input: 3.00m, output: 15.00m, cache5m: 3.75m, cache1h: 6.00m, cacheRead: 0.30m),

            CreateLlmPricing(ModelProviders.ANTHROPIC_API, "claude-opus-4-5-20251101", "claude-opus-4-5",
                "Claude Opus 4.5 (Direct API)", "Anthropic", effectiveFrom,
                input: 5.00m, output: 25.00m, cache5m: 6.25m, cache1h: 10.00m, cacheRead: 0.50m),

            CreateLlmPricing(ModelProviders.ANTHROPIC_API, "claude-opus-4-1-20250515", "claude-opus-4-1",
                "Claude Opus 4.1 (Direct API)", "Anthropic", effectiveFrom,
                input: 15.00m, output: 75.00m, cache5m: 18.75m, cache1h: 30.00m, cacheRead: 1.50m),

            CreateLlmPricing(ModelProviders.ANTHROPIC_API, "claude-sonnet-4-5-20250929", "claude-sonnet-4-5",
                "Claude Sonnet 4.5 (Direct API)", "Anthropic", effectiveFrom,
                input: 3.00m, output: 15.00m, cache5m: 3.75m, cache1h: 6.00m, cacheRead: 0.30m),

            CreateLlmPricing(ModelProviders.ANTHROPIC_API, "claude-sonnet-4-20250514", "claude-sonnet-4-0",
                "Claude Sonnet 4 (Direct API)", "Anthropic", effectiveFrom,
                input: 3.00m, output: 15.00m, cache5m: 3.75m, cache1h: 6.00m, cacheRead: 0.30m),

            CreateLlmPricing(ModelProviders.ANTHROPIC_API, "claude-opus-4-20250514", "claude-opus-4-0",
                "Claude Opus 4 (Direct API)", "Anthropic", effectiveFrom,
                input: 15.00m, output: 75.00m, cache5m: 18.75m, cache1h: 30.00m, cacheRead: 1.50m),

            CreateLlmPricing(ModelProviders.ANTHROPIC_API, "claude-3-7-sonnet-20250219", "claude-3-7-sonnet-latest",
                "Claude Sonnet 3.7 (Direct API)", "Anthropic", effectiveFrom,
                input: 3.00m, output: 15.00m, cache5m: 3.75m, cache1h: 6.00m, cacheRead: 0.30m),

            // Haiku 5.5 bills a request whose prompt (input, cache reads and cache writes) is over
            // 100,000 tokens at 5x these rates, for every token of that request.
            CreateLlmPricing(ModelProviders.ANTHROPIC_API, "claude-haiku-5-5", "claude-haiku-5-5",
                "Claude Haiku 5.5 (Direct API)", "Anthropic", effectiveFrom,
                input: 0.10m, output: 0.50m, cache5m: 0.125m, cache1h: 0.20m, cacheRead: 0.01m,
                longPrompt: Haiku55LongPrompt),

            CreateLlmPricing(ModelProviders.ANTHROPIC_API, "claude-haiku-4-5-20251001", "claude-haiku-4-5",
                "Claude Haiku 4.5 (Direct API)", "Anthropic", effectiveFrom,
                input: 1.00m, output: 5.00m, cache5m: 1.25m, cache1h: 2.00m, cacheRead: 0.10m),

            CreateLlmPricing(ModelProviders.ANTHROPIC_API, "claude-3-5-haiku-20241022", "claude-3-5-haiku-latest",
                "Claude Haiku 3.5 (Direct API)", "Anthropic", effectiveFrom,
                input: 0.80m, output: 4.00m, cache5m: 1.00m, cache1h: 1.60m, cacheRead: 0.08m),

            CreateLlmPricing(ModelProviders.ANTHROPIC_API, "claude-3-haiku-20240307", null,
                "Claude Haiku 3 (Direct API)", "Anthropic", effectiveFrom,
                input: 0.25m, output: 1.25m, cache5m: 0.30m, cache1h: 0.50m, cacheRead: 0.03m),

            // ═══════════════════════════════════════════════════════════════
            // AWS BEDROCK - ANTHROPIC MODELS
            // https://aws.amazon.com/bedrock/pricing/
            // ═══════════════════════════════════════════════════════════════

            CreateLlmPricing(ModelProviders.AWS_BEDROCK, "claude-fable-5", "claude-fable-5",
                "Claude Fable 5 (AWS Bedrock)", "Anthropic", effectiveFrom,
                input: 10.00m, output: 50.00m, cache5m: 12.50m, cache1h: 20.00m, cacheRead: 1.00m,
                regionalEndpointMultiplier: BEDROCK_REGIONAL_ENDPOINT_MULTIPLIER),

            CreateLlmPricing(ModelProviders.AWS_BEDROCK, "claude-opus-5-5", "claude-opus-5-5",
                "Claude Opus 5.5 (AWS Bedrock)", "Anthropic", effectiveFrom,
                input: 4.00m, output: 20.00m, cache5m: 5.00m, cache1h: 8.00m, cacheRead: 0.20m,
                regionalEndpointMultiplier: BEDROCK_REGIONAL_ENDPOINT_MULTIPLIER),

            CreateLlmPricing(ModelProviders.AWS_BEDROCK, "claude-opus-5", "claude-opus-5",
                "Claude Opus 5 (AWS Bedrock)", "Anthropic", effectiveFrom,
                input: 5.00m, output: 25.00m, cache5m: 6.25m, cache1h: 10.00m, cacheRead: 0.50m,
                regionalEndpointMultiplier: BEDROCK_REGIONAL_ENDPOINT_MULTIPLIER),

            CreateLlmPricing(ModelProviders.AWS_BEDROCK, "claude-opus-4-8", "claude-opus-4-8",
                "Claude Opus 4.8 (AWS Bedrock)", "Anthropic", effectiveFrom,
                input: 5.00m, output: 25.00m, cache5m: 6.25m, cache1h: 10.00m, cacheRead: 0.50m,
                regionalEndpointMultiplier: BEDROCK_REGIONAL_ENDPOINT_MULTIPLIER),

            CreateLlmPricing(ModelProviders.AWS_BEDROCK, "claude-sonnet-5-5", "claude-sonnet-5-5",
                "Claude Sonnet 5.5 (AWS Bedrock)", "Anthropic", effectiveFrom,
                input: 2.00m, output: 10.00m, cache5m: 2.50m, cache1h: 4.00m, cacheRead: 0.10m,
                regionalEndpointMultiplier: BEDROCK_REGIONAL_ENDPOINT_MULTIPLIER),

            CreateLlmPricing(ModelProviders.AWS_BEDROCK, "claude-sonnet-5", "claude-sonnet-5",
                "Claude Sonnet 5 (AWS Bedrock)", "Anthropic", effectiveFrom,
                input: 2.00m, output: 10.00m, cache5m: 2.50m, cache1h: 4.00m, cacheRead: 0.20m,
                regionalEndpointMultiplier: BEDROCK_REGIONAL_ENDPOINT_MULTIPLIER),

            CreateLlmPricing(ModelProviders.AWS_BEDROCK, "claude-opus-4-6", "claude-opus-4-6",
                "Claude Opus 4.6 (AWS Bedrock)", "Anthropic", effectiveFrom,
                input: 5.00m, output: 25.00m, cache5m: 6.25m, cache1h: 10.00m, cacheRead: 0.50m,
                regionalEndpointMultiplier: BEDROCK_REGIONAL_ENDPOINT_MULTIPLIER),

            CreateLlmPricing(ModelProviders.AWS_BEDROCK, "claude-sonnet-4-6", "claude-sonnet-4-6",
                "Claude Sonnet 4.6 (AWS Bedrock)", "Anthropic", effectiveFrom,
                input: 3.00m, output: 15.00m, cache5m: 3.75m, cache1h: 6.00m, cacheRead: 0.30m,
                regionalEndpointMultiplier: BEDROCK_REGIONAL_ENDPOINT_MULTIPLIER),

            CreateLlmPricing(ModelProviders.AWS_BEDROCK, "claude-opus-4-5-20251101", "claude-opus-4-5",
                "Claude Opus 4.5 (AWS Bedrock)", "Anthropic", effectiveFrom,
                input: 5.00m, output: 25.00m, cache5m: 6.25m, cache1h: 10.00m, cacheRead: 0.50m,
                regionalEndpointMultiplier: BEDROCK_REGIONAL_ENDPOINT_MULTIPLIER),

            CreateLlmPricing(ModelProviders.AWS_BEDROCK, "claude-opus-4-1-20250515", "claude-opus-4-1",
                "Claude Opus 4.1 (AWS Bedrock)", "Anthropic", effectiveFrom,
                input: 15.00m, output: 75.00m, cache5m: 18.75m, cache1h: 30.00m, cacheRead: 1.50m),

            CreateLlmPricing(ModelProviders.AWS_BEDROCK, "claude-opus-4-20250514", "claude-opus-4-0",
                "Claude Opus 4 (AWS Bedrock)", "Anthropic", effectiveFrom,
                input: 15.00m, output: 75.00m, cache5m: 18.75m, cache1h: 30.00m, cacheRead: 1.50m),

            CreateLlmPricing(ModelProviders.AWS_BEDROCK, "claude-sonnet-4-5-20250929", "claude-sonnet-4-5",
                "Claude Sonnet 4.5 (AWS Bedrock)", "Anthropic", effectiveFrom,
                input: 3.00m, output: 15.00m, cache5m: 3.75m, cache1h: 6.00m, cacheRead: 0.30m,
                regionalEndpointMultiplier: BEDROCK_REGIONAL_ENDPOINT_MULTIPLIER),

            CreateLlmPricing(ModelProviders.AWS_BEDROCK, "claude-sonnet-4-20250514", "claude-sonnet-4-0",
                "Claude Sonnet 4 (AWS Bedrock)", "Anthropic", effectiveFrom,
                input: 3.00m, output: 15.00m, cache5m: 3.75m, cache1h: 6.00m, cacheRead: 0.30m),

            CreateLlmPricing(ModelProviders.AWS_BEDROCK, "claude-3-7-sonnet-20250219", "claude-3-7-sonnet-latest",
                "Claude Sonnet 3.7 (AWS Bedrock)", "Anthropic", effectiveFrom,
                input: 3.00m, output: 15.00m, cache5m: 3.75m, cache1h: 6.00m, cacheRead: 0.30m),

            CreateLlmPricing(ModelProviders.AWS_BEDROCK, "claude-haiku-5-5", "claude-haiku-5-5",
                "Claude Haiku 5.5 (AWS Bedrock)", "Anthropic", effectiveFrom,
                input: 0.10m, output: 0.50m, cache5m: 0.125m, cache1h: 0.20m, cacheRead: 0.01m,
                longPrompt: Haiku55LongPrompt,
                regionalEndpointMultiplier: BEDROCK_REGIONAL_ENDPOINT_MULTIPLIER),

            CreateLlmPricing(ModelProviders.AWS_BEDROCK, "claude-haiku-4-5-20251001", "claude-haiku-4-5",
                "Claude Haiku 4.5 (AWS Bedrock)", "Anthropic", effectiveFrom,
                input: 1.00m, output: 5.00m, cache5m: 1.25m, cache1h: 2.00m, cacheRead: 0.10m,
                regionalEndpointMultiplier: BEDROCK_REGIONAL_ENDPOINT_MULTIPLIER),

            CreateLlmPricing(ModelProviders.AWS_BEDROCK, "claude-3-5-haiku-20241022", "claude-3-5-haiku-latest",
                "Claude Haiku 3.5 (AWS Bedrock)", "Anthropic", effectiveFrom,
                input: 0.80m, output: 4.00m, cache5m: 1.00m, cache1h: 1.60m, cacheRead: 0.08m),

            CreateLlmPricing(ModelProviders.AWS_BEDROCK, "claude-3-haiku-20240307", null,
                "Claude Haiku 3 (AWS Bedrock)", "Anthropic", effectiveFrom,
                input: 0.25m, output: 1.25m, cache5m: 0.30m, cache1h: 0.50m, cacheRead: 0.03m),

            // ═══════════════════════════════════════════════════════════════
            // AZURE AI FOUNDRY - ANTHROPIC MODELS
            // Keyed on the Foundry deployment name the stack configures (the platform's
            // AgentRuntime:AllowedModels mirror of the runtime's allowlist), at the same list
            // rates as the Bedrock rows for the same model. A deployment named after a model it
            // does not serve needs its own row under its own name.
            // ═══════════════════════════════════════════════════════════════

            CreateLlmPricing(ModelProviders.AZURE_FOUNDRY, "claude-opus-5-5", "claude-opus-5-5",
                "Claude Opus 5.5 (Azure AI Foundry)", "Anthropic", effectiveFrom,
                input: 4.00m, output: 20.00m, cache5m: 5.00m, cache1h: 8.00m, cacheRead: 0.20m),

            CreateLlmPricing(ModelProviders.AZURE_FOUNDRY, "claude-opus-5", "claude-opus-5",
                "Claude Opus 5 (Azure AI Foundry)", "Anthropic", effectiveFrom,
                input: 5.00m, output: 25.00m, cache5m: 6.25m, cache1h: 10.00m, cacheRead: 0.50m),

            CreateLlmPricing(ModelProviders.AZURE_FOUNDRY, "claude-opus-4-8", "claude-opus-4-8",
                "Claude Opus 4.8 (Azure AI Foundry)", "Anthropic", effectiveFrom,
                input: 5.00m, output: 25.00m, cache5m: 6.25m, cache1h: 10.00m, cacheRead: 0.50m),

            CreateLlmPricing(ModelProviders.AZURE_FOUNDRY, "claude-sonnet-5-5", "claude-sonnet-5-5",
                "Claude Sonnet 5.5 (Azure AI Foundry)", "Anthropic", effectiveFrom,
                input: 2.00m, output: 10.00m, cache5m: 2.50m, cache1h: 4.00m, cacheRead: 0.10m),

            CreateLlmPricing(ModelProviders.AZURE_FOUNDRY, "claude-sonnet-5", "claude-sonnet-5",
                "Claude Sonnet 5 (Azure AI Foundry)", "Anthropic", effectiveFrom,
                input: 2.00m, output: 10.00m, cache5m: 2.50m, cache1h: 4.00m, cacheRead: 0.20m),

            CreateLlmPricing(ModelProviders.AZURE_FOUNDRY, "claude-sonnet-4-6", "claude-sonnet-4-6",
                "Claude Sonnet 4.6 (Azure AI Foundry)", "Anthropic", effectiveFrom,
                input: 3.00m, output: 15.00m, cache5m: 3.75m, cache1h: 6.00m, cacheRead: 0.30m),

            CreateLlmPricing(ModelProviders.AZURE_FOUNDRY, "claude-haiku-5-5", "claude-haiku-5-5",
                "Claude Haiku 5.5 (Azure AI Foundry)", "Anthropic", effectiveFrom,
                input: 0.10m, output: 0.50m, cache5m: 0.125m, cache1h: 0.20m, cacheRead: 0.01m,
                longPrompt: Haiku55LongPrompt),

            CreateLlmPricing(ModelProviders.AZURE_FOUNDRY, "claude-haiku-4-5-20251001", "claude-haiku-4-5",
                "Claude Haiku 4.5 (Azure AI Foundry)", "Anthropic", effectiveFrom,
                input: 1.00m, output: 5.00m, cache5m: 1.25m, cache1h: 2.00m, cacheRead: 0.10m),

            // ═══════════════════════════════════════════════════════════════
            // EMBEDDING PROVIDERS
            // https://cohere.com/pricing
            // ═══════════════════════════════════════════════════════════════

            CreateEmbeddingPricing(ModelProviders.COHERE_API, "embed-english-v3.0", null,
                "Cohere Embed English v3", "Cohere", effectiveFrom, 0.10m),

            CreateEmbeddingPricing(ModelProviders.COHERE_API, "embed-multilingual-v3.0", null,
                "Cohere Embed Multilingual v3", "Cohere", effectiveFrom, 0.10m),

            CreateEmbeddingPricing(ModelProviders.COHERE_API, "embed-v4.0", null,
                "Cohere Embed v4", "Cohere", effectiveFrom, 0.12m),

            // AWS Bedrock Cohere embeddings
            CreateEmbeddingPricing(ModelProviders.AWS_BEDROCK, "cohere.embed-english-v3", null,
                "Cohere Embed English v3 (AWS Bedrock)", "Cohere", effectiveFrom, 0.10m),

            CreateEmbeddingPricing(ModelProviders.AWS_BEDROCK, "cohere.embed-multilingual-v3", null,
                "Cohere Embed Multilingual v3 (AWS Bedrock)", "Cohere", effectiveFrom, 0.10m),

            CreateEmbeddingPricing(ModelProviders.AWS_BEDROCK, "global.cohere.embed-v4:0", "cohere.embed-v4",
                "Cohere Embed v4 (AWS Bedrock)", "Cohere", effectiveFrom, 0.10m),

        ];
    }

    /// <summary>
    /// Creates an LLM pricing record with optional cache components.
    /// </summary>
    private static ModelPricing CreateLlmPricing(
        string provider, string modelId, string? modelAlias, string displayName,
        string family, DateTime effectiveFrom,
        decimal input, decimal output,
        decimal? cache5m = null, decimal? cache1h = null, decimal? cacheRead = null,
        string currency = "USD", LongPromptTier? longPrompt = null,
        decimal? regionalEndpointMultiplier = null)
    {
        string pricingId = Guid.NewGuid().ToString();
        var components = new List<ModelPricingComponent>
        {
            new()
            {
                Id = Guid.NewGuid().ToString(),
                ModelPricingId = pricingId,
                ComponentType = PricingComponentType.Input,
                Cost = input,
                Unit = PricingUnit.PerMillion,
                Currency = currency
            },
            new()
            {
                Id = Guid.NewGuid().ToString(),
                ModelPricingId = pricingId,
                ComponentType = PricingComponentType.Output,
                Cost = output,
                Unit = PricingUnit.PerMillion,
                Currency = currency
            }
        };

        if (cache5m.HasValue)
        {
            components.Add(new ModelPricingComponent
            {
                Id = Guid.NewGuid().ToString(),
                ModelPricingId = pricingId,
                ComponentType = PricingComponentType.CacheWrite5m,
                Cost = cache5m.Value,
                Unit = PricingUnit.PerMillion,
                Currency = currency
            });
        }

        if (cache1h.HasValue)
        {
            components.Add(new ModelPricingComponent
            {
                Id = Guid.NewGuid().ToString(),
                ModelPricingId = pricingId,
                ComponentType = PricingComponentType.CacheWrite1h,
                Cost = cache1h.Value,
                Unit = PricingUnit.PerMillion,
                Currency = currency
            });
        }

        if (cacheRead.HasValue)
        {
            components.Add(new ModelPricingComponent
            {
                Id = Guid.NewGuid().ToString(),
                ModelPricingId = pricingId,
                ComponentType = PricingComponentType.CacheRead,
                Cost = cacheRead.Value,
                Unit = PricingUnit.PerMillion,
                Currency = currency
            });
        }

        if (longPrompt is not null)
        {
            components.AddRange(
            [
                Component(pricingId, PricingComponentType.LongPromptInput, longPrompt.Input, currency),
                Component(pricingId, PricingComponentType.LongPromptOutput, longPrompt.Output, currency),
                Component(pricingId, PricingComponentType.LongPromptCacheWrite5m, longPrompt.Cache5m, currency),
                Component(pricingId, PricingComponentType.LongPromptCacheWrite1h, longPrompt.Cache1h, currency),
                Component(pricingId, PricingComponentType.LongPromptCacheRead, longPrompt.CacheRead, currency),
            ]);
        }

        return new ModelPricing
        {
            Id = pricingId,
            Provider = provider,
            Family = family,
            ModelId = modelId,
            ModelAlias = modelAlias,
            DisplayName = displayName,
            PricingType = PricingType.Llm,
            EffectiveFrom = effectiveFrom,
            IsActive = true,
            LongPromptThresholdTokens = longPrompt?.ThresholdTokens,
            RegionalEndpointMultiplier = regionalEndpointMultiplier,
            Components = components
        };
    }

    private static ModelPricingComponent Component(
        string pricingId, PricingComponentType componentType, decimal cost, string currency)
    {
        return new ModelPricingComponent
        {
            Id = Guid.NewGuid().ToString(),
            ModelPricingId = pricingId,
            ComponentType = componentType,
            Cost = cost,
            Unit = PricingUnit.PerMillion,
            Currency = currency
        };
    }

    /// <summary>
    /// Creates an embedding pricing record.
    /// </summary>
    private static ModelPricing CreateEmbeddingPricing(
        string provider, string modelId, string? modelAlias, string displayName,
        string family, DateTime effectiveFrom, decimal embeddingCost, string currency = "USD")
    {
        string pricingId = Guid.NewGuid().ToString();

        return new ModelPricing
        {
            Id = pricingId,
            Provider = provider,
            Family = family,
            ModelId = modelId,
            ModelAlias = modelAlias,
            DisplayName = displayName,
            PricingType = PricingType.Embedding,
            EffectiveFrom = effectiveFrom,
            IsActive = true,
            Components =
            [
                new ModelPricingComponent
                {
                    Id = Guid.NewGuid().ToString(),
                    ModelPricingId = pricingId,
                    ComponentType = PricingComponentType.Embedding,
                    Cost = embeddingCost,
                    Unit = PricingUnit.PerMillion,
                    Currency = currency
                }
            ]
        };
    }
}

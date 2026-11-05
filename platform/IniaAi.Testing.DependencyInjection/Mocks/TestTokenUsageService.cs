using IniaAi.Common.Interfaces.Services;
using IniaAi.Domain.Entities.TokenUsage;

namespace IniaAi.Testing.DependencyInjection.Mocks;

/// <summary>
/// Test implementation of ITokenUsageService that captures recorded usage for verification.
/// Thread-safe for concurrent test scenarios.
/// </summary>
public class TestTokenUsageService : ITokenUsageService
{
    /// <summary>
    /// What an unpriced unit costs here, so the cost a caller accumulates from the return value
    /// is a number a test can predict from the tokens it scripted.
    /// </summary>
    public const decimal PRICE_PER_UNIT = 0.001m;

    private readonly List<LlmUsageRecord> _llmRecords = [];
    private readonly List<EmbeddingUsageRecord> _embeddingRecords = [];
    private readonly Lock _lock = new();

    /// <summary>
    /// Gets all LLM usage records that have been recorded.
    /// </summary>
    public IReadOnlyList<LlmUsageRecord> LlmRecords
    {
        get
        {
            lock (_lock)
            {
                return [.. _llmRecords];
            }
        }
    }

    /// <summary>
    /// Gets all embedding usage records that have been recorded.
    /// </summary>
    public IReadOnlyList<EmbeddingUsageRecord> EmbeddingRecords
    {
        get
        {
            lock (_lock)
            {
                return [.. _embeddingRecords];
            }
        }
    }

    public Task<decimal> RecordLlmUsageAsync(LlmUsageRecord record, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _llmRecords.Add(record);
        }

        return Task.FromResult(record.Components.Sum(c => c.CostUsd ?? c.Units * PRICE_PER_UNIT));
    }

    /// <summary>
    /// The pricing rows a test registers, keyed by provider and model id. Empty by default, so a
    /// caller that prices its own requests finds no row and falls back to its unpriced path.
    /// </summary>
    public Dictionary<(string Provider, string ModelId), ModelPricing> Pricings { get; } = [];

    /// <summary>
    /// When set, a pricing lookup completes asynchronously, as the real one does against the
    /// database, so a caller that does not await its billing loses the records it writes after.
    /// </summary>
    public bool PricingLookupYields { get; set; }

    public async Task<ModelPricing?> FindLlmPricingAsync(string provider, string modelId, CancellationToken cancellationToken = default)
    {
        if (PricingLookupYields)
        {
            await Task.Yield();
        }

        lock (_lock)
        {
            return Pricings.GetValueOrDefault((provider, modelId));
        }
    }

    public Task RecordEmbeddingUsageAsync(EmbeddingUsageRecord record, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _embeddingRecords.Add(record);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Clears all recorded usage data. Useful for test isolation.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _llmRecords.Clear();
            _embeddingRecords.Clear();
        }
    }
}

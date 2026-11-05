using IniaAi.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Domain.Entities.TokenUsage;

/// <summary>
/// Ledger rows summed into one row, by the component types <see cref="TokenUsageTotalComponents"/>
/// names. The usage-totals sweep is the only writer: it adds each ledger row once and marks it
/// <see cref="TokenUsage.IsInTotals"/> in the same transaction, so a read that adds the rows not
/// yet marked counts every ledger row exactly once.
/// </summary>
public abstract class TokenUsageTotal<T> : Entity<T>
    where T : class
{
    private static readonly Func<TokenUsageComponent, bool> _isInput = TokenUsageTotalComponents.IsInput.Compile();
    private static readonly Func<TokenUsageComponent, bool> _isOutput = TokenUsageTotalComponents.IsOutput.Compile();
    private static readonly Func<TokenUsageComponent, bool> _isCacheWrite = TokenUsageTotalComponents.IsCacheWrite.Compile();
    private static readonly Func<TokenUsageComponent, bool> _isCacheRead = TokenUsageTotalComponents.IsCacheRead.Compile();
    private static readonly Func<TokenUsageComponent, bool> _isEmbedding = TokenUsageTotalComponents.IsEmbedding.Compile();

    public long InputTokens { get; set; }

    public long OutputTokens { get; set; }

    public long CacheWriteTokens { get; set; }

    public long CacheReadTokens { get; set; }

    public long EmbeddingTokens { get; set; }

    [Precision(18, 10)]
    public decimal Cost { get; set; }

    /// <summary>
    /// How many ledger rows the totals hold.
    /// </summary>
    public int OperationCount { get; set; }

    public void Add(TokenUsageComponent component)
    {
        InputTokens += _isInput(component) ? component.Units : 0;
        OutputTokens += _isOutput(component) ? component.Units : 0;
        CacheWriteTokens += _isCacheWrite(component) ? component.Units : 0;
        CacheReadTokens += _isCacheRead(component) ? component.Units : 0;
        EmbeddingTokens += _isEmbedding(component) ? component.Units : 0;
        Cost += component.Cost;
    }
}

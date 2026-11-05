namespace IniaAi.WebApi.Contracts.Responses.TokenUsage;

/// <summary>
/// What one workflow run cost, from its stored token usage rows: the total, one line per block,
/// and one line per kind of call that belongs to no block (document processing, for example).
/// </summary>
public sealed record ExecutionCostDto
{
    /// <summary>The workflow execution.</summary>
    public string ExecutionId { get; init; } = string.Empty;

    /// <summary>The sum of every line, in US dollars.</summary>
    public decimal TotalCost { get; init; }

    /// <summary>How many usage rows the figures come from.</summary>
    public int UsageRecordCount { get; init; }

    /// <summary>Usage rows that recorded no cost because no price matched their model.</summary>
    public int UnpricedRecordCount { get; init; }

    /// <summary>One line per block that spent, most expensive first.</summary>
    public List<ExecutionCostLineDto> Blocks { get; init; } = [];

    /// <summary>Spend that belongs to no block, one line per usage type.</summary>
    public List<ExecutionCostLineDto> Other { get; init; } = [];
}

/// <summary>
/// The spend of one block, or of one kind of call outside any block.
/// </summary>
public sealed record ExecutionCostLineDto
{
    /// <summary>The block, when the line is a block's.</summary>
    public string? BlockId { get; init; }

    /// <summary>The block's name, or the usage type's name for a line outside any block.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The block's replacement tag.</summary>
    public string? ReplacementTag { get; init; }

    /// <summary>The block type.</summary>
    public string? BlockType { get; init; }

    /// <summary>Every model the line's calls used.</summary>
    public List<string> Models { get; init; } = [];

    /// <summary>How many usage rows the line sums.</summary>
    public int Calls { get; init; }

    /// <summary>Input tokens, not counting cache reads and writes.</summary>
    public long InputTokens { get; init; }

    /// <summary>Output tokens, reasoning included.</summary>
    public long OutputTokens { get; init; }

    /// <summary>Tokens read from the prompt cache.</summary>
    public long CacheReadTokens { get; init; }

    /// <summary>Tokens written to the prompt cache.</summary>
    public long CacheWriteTokens { get; init; }

    /// <summary>The line's cost in US dollars.</summary>
    public decimal Cost { get; init; }
}

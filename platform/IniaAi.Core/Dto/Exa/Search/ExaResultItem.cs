namespace IniaAi.Core.Dto.Exa.Search;

public record ExaResultItem
{
    public required string Url { get; init; }

    public string? Title { get; init; }

    public required string Id { get; init; }

    public double? Score { get; init; }

    public string? PublishedDate { get; init; }

    public string? Author { get; init; }

    public string? Text { get; init; }

    public string[]? Highlights { get; init; }

    public double[]? HighlightScores { get; init; }

    public object? Summary { get; init; } // Can be string or JsonElement for structured summaries

    public string? Image { get; init; }

    public string? Favicon { get; init; }

    public ExaResultItem[]? Subpages { get; init; }
}

namespace IniaAi.Core.Dto.Exa.Search;

public record ExaSearchResponse
{
    public ExaResultItem[] Results { get; init; } = [];

    public string? AutopromptString { get; init; }

    public string? RequestId { get; init; }
}

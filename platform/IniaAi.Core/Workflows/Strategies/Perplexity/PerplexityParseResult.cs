using IniaAi.Core.Workflows.Annotations;

namespace IniaAi.Core.Workflows.Strategies.Perplexity;

public class PerplexityParseResult
{
    /// <summary>
    /// The original response body with `[N]` marker runs stripped from the
    /// answer content. JSON structure and all other fields are preserved.
    /// </summary>
    public string CleanedBody { get; init; } = string.Empty;
    public IReadOnlyList<Fact> Facts { get; init; } = [];
}

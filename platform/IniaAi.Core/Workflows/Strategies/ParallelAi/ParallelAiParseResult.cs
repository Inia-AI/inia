using IniaAi.Core.Workflows.Annotations;

namespace IniaAi.Core.Workflows.Strategies.ParallelAi;

public class ParallelAiParseResult
{
    /// <summary>
    /// The response body passed through unchanged. Parallel.ai responses carry
    /// no inline markers to strip, so this equals the original body.
    /// </summary>
    public string CleanedBody { get; init; } = string.Empty;
    public IReadOnlyList<Fact> Facts { get; init; } = [];
}

using IniaAi.Application.Commands.TextProcessing.RefineContent;
using IniaAi.Common.Interfaces.Services;

namespace IniaAi.Application.Common.Interfaces.Services;

/// <summary>
/// Service for iteratively refining content using AI.
/// </summary>
public interface IContentRefinementService : ITokenAccumulator
{
    /// <summary>
    /// Refines the given content based on the user's instruction and conversation history.
    /// </summary>
    /// <param name="content">The content to refine.</param>
    /// <param name="instruction">The user's instruction for refinement.</param>
    /// <param name="conversationHistory">Previous turns in the refinement session.</param>
    /// <param name="organizationId">The organization ID for model resolution.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The refined content and explanation, an out-of-scope signal, or null on failure.</returns>
    Task<ContentRefinementOutput?> RefineAsync(
        string content,
        string instruction,
        IReadOnlyList<RefinementTurnDto> conversationHistory,
        string? organizationId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the output of a content refinement operation.
/// Construct via <see cref="Refined"/> for a successful refinement or <see cref="OutOfScope"/> for a refusal.
/// When <see cref="IsOutOfScope"/> is true, <see cref="RefinedContent"/> and <see cref="Explanation"/> are guaranteed null.
/// </summary>
public sealed record ContentRefinementOutput
{
    public string? RefinedContent { get; }
    public string? Explanation { get; }
    public bool IsOutOfScope { get; }

    private ContentRefinementOutput(string? refinedContent, string? explanation, bool isOutOfScope)
    {
        RefinedContent = refinedContent;
        Explanation = explanation;
        IsOutOfScope = isOutOfScope;
    }

    public static ContentRefinementOutput Refined(string refinedContent, string? explanation) =>
        new(refinedContent, explanation, isOutOfScope: false);

    public static ContentRefinementOutput OutOfScope() =>
        new(refinedContent: null, explanation: null, isOutOfScope: true);
}

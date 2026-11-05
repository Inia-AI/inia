namespace IniaAi.WebApi.Models.Requests.ContentRefinement;

/// <summary>
/// Represents a request to refine content using AI.
/// </summary>
public class RefineContentRequest
{
    /// <summary>
    /// Gets or sets the content to refine (full document or selected fragment).
    /// </summary>
    public required string Content { get; set; }

    /// <summary>
    /// Gets or sets the user instruction for refinement (typed or from a preset).
    /// </summary>
    public required string Instruction { get; set; }

    /// <summary>
    /// Gets or sets the conversation history from previous iterations in this session.
    /// </summary>
    public List<RefinementTurn> ConversationHistory { get; set; } = [];
}

/// <summary>
/// Represents a single turn in the refinement conversation.
/// </summary>
public class RefinementTurn
{
    /// <summary>
    /// Gets or sets the role of the speaker ("user" or "assistant").
    /// </summary>
    public required string Role { get; set; }

    /// <summary>
    /// Gets or sets the content of this conversation turn.
    /// </summary>
    public required string Content { get; set; }
}

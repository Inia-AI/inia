using ErrorOr;
using IniaAi.Application.Commands.TextProcessing.RefineContent.Models;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Commands.TextProcessing.RefineContent;

/// <summary>
/// Command to refine content using AI with iterative conversation support.
/// </summary>
public class RefineContentCommand : IRequest<ErrorOr<RefineContentResult>>
{
    public RefineContentCommand(
        string organizationId,
        string userId,
        string content,
        string instruction,
        IReadOnlyList<RefinementTurnDto> conversationHistory)
    {
        OrganizationId = organizationId;
        UserId = userId;
        Content = content;
        Instruction = instruction;
        ConversationHistory = conversationHistory;
    }

    /// <summary>
    /// Gets the organization ID.
    /// </summary>
    public string OrganizationId { get; }

    /// <summary>
    /// Gets the user ID.
    /// </summary>
    public string UserId { get; }

    /// <summary>
    /// Gets the content to refine.
    /// </summary>
    public string Content { get; }

    /// <summary>
    /// Gets the user instruction for refinement.
    /// </summary>
    public string Instruction { get; }

    /// <summary>
    /// Gets the conversation history from previous iterations.
    /// </summary>
    public IReadOnlyList<RefinementTurnDto> ConversationHistory { get; }
}

/// <summary>
/// Represents a single turn in the refinement conversation.
/// </summary>
public record RefinementTurnDto(string Role, string Content);

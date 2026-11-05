using System.Globalization;
using FluentValidation;
using IniaAi.Configuration.Platform;
using Microsoft.Extensions.Options;

namespace IniaAi.Application.Commands.TextProcessing.RefineContent;

/// <summary>
/// Validator for the <see cref="RefineContentCommand"/>.
/// </summary>
public class RefineContentCommandValidator : AbstractValidator<RefineContentCommand>
{
    private const int MaxInstructionLength = 10_000;
    private const int MaxConversationTurns = 20;
    private const int MaxTurnContentLength = 100_000;

    public RefineContentCommandValidator(IOptions<ContentRefinementSettings>? settings = null)
    {
        ContentRefinementSettings limits = settings?.Value ?? new ContentRefinementSettings();
        int maxContentLength = limits.MaxContentLength;
        int maxCombinedLength = limits.MaxCombinedLength;

        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();

        _ = RuleFor(x => x.Content)
            .NotEmpty()
            .WithMessage("Content is required for refinement.")
            .MaximumLength(maxContentLength)
            .WithMessage(cmd => string.Create(
                CultureInfo.InvariantCulture,
                $"The paragraph is {cmd.Content.Length:N0} characters, more than the {maxContentLength:N0} that refine takes. Shorten the paragraph, then refine it again."));

        _ = RuleFor(x => x.Instruction)
            .NotEmpty()
            .WithMessage("An instruction is required for refinement.")
            .MaximumLength(MaxInstructionLength)
            .WithMessage($"Instruction cannot exceed {MaxInstructionLength} characters.");

        _ = RuleFor(x => x.ConversationHistory)
            .Must(h => h.Count <= MaxConversationTurns)
            .WithMessage($"Conversation history cannot exceed {MaxConversationTurns} turns.")
            .Must(IsAlternatingUserAssistant)
            .WithMessage("Conversation history must be in strict user/assistant pairs, starting with 'user' and alternating.");

        _ = RuleForEach(x => x.ConversationHistory)
            .ChildRules(turn =>
            {
                _ = turn.RuleFor(t => t.Role)
                    .Must(r => r is "user" or "assistant")
                    .WithMessage("Conversation turn role must be 'user' or 'assistant'.");
                _ = turn.RuleFor(t => t.Content)
                    .NotEmpty()
                    .MaximumLength(MaxTurnContentLength)
                    .WithMessage($"Conversation turn content cannot exceed {MaxTurnContentLength} characters.");
            });

        _ = RuleFor(x => x)
            .Must(cmd => GetCombinedLength(cmd) <= maxCombinedLength)
            .When(cmd => cmd.Content.Length <= maxContentLength)
            .WithMessage(string.Create(
                CultureInfo.InvariantCulture,
                $"This refine conversation is longer than the {maxCombinedLength:N0} characters that refine takes. Close the panel, then start a new refine."));
    }

    private static bool IsAlternatingUserAssistant(IReadOnlyList<RefinementTurnDto> history)
    {
        if (history is null)
        {
            return true;
        }

        if (history.Count % 2 != 0)
        {
            return false;
        }

        for (int i = 0; i < history.Count; i++)
        {
            string expected = i % 2 == 0 ? "user" : "assistant";
            if (!string.Equals(history[i].Role, expected, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static int GetCombinedLength(RefineContentCommand cmd)
    {
        int total = (cmd.Content?.Length ?? 0) + (cmd.Instruction?.Length ?? 0);
        if (cmd.ConversationHistory is null)
        {
            return total;
        }

        foreach (RefinementTurnDto turn in cmd.ConversationHistory)
        {
            total += turn.Content?.Length ?? 0;
        }

        return total;
    }
}

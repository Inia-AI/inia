using FluentValidation;

namespace IniaAi.Application.Commands.Signatures.AcceptSuggestion;

public class AcceptSuggestionCommandValidator : AbstractValidator<AcceptSuggestionCommand>
{
    public AcceptSuggestionCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.PendingOverrideId).NotEmpty();
        _ = RuleFor(x => x.VoteId).NotEmpty();
    }
}

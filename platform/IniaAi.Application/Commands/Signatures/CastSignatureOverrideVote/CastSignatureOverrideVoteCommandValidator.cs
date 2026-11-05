using FluentValidation;
using IniaAi.Domain.Entities.Signatures;

namespace IniaAi.Application.Commands.Signatures.CastSignatureOverrideVote;

public class CastSignatureOverrideVoteCommandValidator
    : AbstractValidator<CastSignatureOverrideVoteCommand>
{
    private const int COMMENT_MAX_LENGTH = 2000;
    private const int SUGGESTED_VALUE_MAX_LENGTH = 4000;

    public CastSignatureOverrideVoteCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.PendingOverrideId).NotEmpty();
        _ = RuleFor(x => x.VoteKind)
            .Must(v => v is SignatureVoteKind.Approve or SignatureVoteKind.Reject)
            .WithMessage("VoteKind must be Approve or Reject.");
        _ = RuleFor(x => x.Comment)
            .MaximumLength(COMMENT_MAX_LENGTH)
            .When(x => x.Comment is not null);
        _ = RuleFor(x => x.SuggestedValue)
            .MaximumLength(SUGGESTED_VALUE_MAX_LENGTH)
            .When(x => x.SuggestedValue is not null);
    }
}

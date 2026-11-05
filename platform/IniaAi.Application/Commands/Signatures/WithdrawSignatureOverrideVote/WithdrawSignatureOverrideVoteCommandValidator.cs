using FluentValidation;

namespace IniaAi.Application.Commands.Signatures.WithdrawSignatureOverrideVote;

public class WithdrawSignatureOverrideVoteCommandValidator : AbstractValidator<WithdrawSignatureOverrideVoteCommand>
{
    public WithdrawSignatureOverrideVoteCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.PendingOverrideId).NotEmpty();
    }
}

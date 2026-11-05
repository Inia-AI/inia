using FluentValidation;

namespace IniaAi.Application.Commands.Signatures.CancelPendingSignatureOverride;

public class CancelPendingSignatureOverrideCommandValidator : AbstractValidator<CancelPendingSignatureOverrideCommand>
{
    public CancelPendingSignatureOverrideCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.PendingOverrideId).NotEmpty();
    }
}

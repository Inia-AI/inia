using FluentValidation;

namespace IniaAi.Application.Commands.Signatures.SweepPendingSignatureOverrides;

public class SweepPendingSignatureOverridesCommandValidator
    : AbstractValidator<SweepPendingSignatureOverridesCommand>
{
    public SweepPendingSignatureOverridesCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
    }
}

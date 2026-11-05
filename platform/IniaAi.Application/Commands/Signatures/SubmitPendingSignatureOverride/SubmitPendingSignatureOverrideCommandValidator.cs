using FluentValidation;

namespace IniaAi.Application.Commands.Signatures.SubmitPendingSignatureOverride;

public class SubmitPendingSignatureOverrideCommandValidator
    : AbstractValidator<SubmitPendingSignatureOverrideCommand>
{
    private const int PROPOSED_VALUE_MAX_LENGTH = 4000;
    private const int PLACEHOLDER_MAX_LENGTH = 200;
    private const int WORKFLOW_TYPE_MAX_LENGTH = 200;

    public SubmitPendingSignatureOverrideCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.WorkflowTypeId).NotEmpty().MaximumLength(WORKFLOW_TYPE_MAX_LENGTH);
        _ = RuleFor(x => x.PlaceholderName).NotEmpty().MaximumLength(PLACEHOLDER_MAX_LENGTH);
        _ = RuleFor(x => x.ProposedValue)
            .MaximumLength(PROPOSED_VALUE_MAX_LENGTH)
            .When(x => x.ProposedValue is not null);
    }
}

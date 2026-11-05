using FluentValidation;

namespace IniaAi.Application.Commands.Signatures.TestSignaturePrompt;

public class TestSignaturePromptCommandValidator : AbstractValidator<TestSignaturePromptCommand>
{
    // Match the bounds used by the sibling signatures validators
    // (SubmitPendingSignatureOverrideCommandValidator) so both endpoints treat
    // the same-shaped inputs consistently. TestValue gets the larger 64 KB cap
    // because capture already tolerates values up to that size
    // (AiQueryProperties.MAX_CAPTURED_VALUE_LENGTH); keeping capture-side and
    // replay-side limits in lockstep avoids asymmetric-limit surprises.
    private const int WORKFLOW_EXECUTION_ID_MAX_LENGTH = 200;
    private const int BLOCK_ID_MAX_LENGTH = 64;
    private const int PLACEHOLDER_MAX_LENGTH = 200;
    private const int TEST_VALUE_MAX_LENGTH = 64 * 1024;

    public TestSignaturePromptCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.WorkflowExecutionId).NotEmpty().MaximumLength(WORKFLOW_EXECUTION_ID_MAX_LENGTH);
        RuleFor(x => x.BlockId).NotEmpty().MaximumLength(BLOCK_ID_MAX_LENGTH);
        RuleFor(x => x.PlaceholderName).NotEmpty().MaximumLength(PLACEHOLDER_MAX_LENGTH);
        RuleFor(x => x.TestValue).NotEmpty().MaximumLength(TEST_VALUE_MAX_LENGTH);
    }
}

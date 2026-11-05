using FluentValidation;

namespace IniaAi.Application.Commands.WorkflowExecutions.StartWorkflowExecution;

/// <summary>
/// Validator for <see cref="StartWorkflowExecutionCommand"/>.
/// Validates structure and business rules.
/// </summary>
public class StartWorkflowExecutionCommandValidator : AbstractValidator<StartWorkflowExecutionCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StartWorkflowExecutionCommandValidator"/> class.
    /// </summary>
    public StartWorkflowExecutionCommandValidator()
    {
        _ = RuleFor(x => x.UserId)
           .NotEmpty().WithMessage("User ID must be provided.");

        _ = RuleFor(x => x.OrganizationId)
            .NotEmpty().WithMessage("Organization ID must be provided.");

        _ = RuleFor(x => x.Request).NotNull();

        _ = When(x => x.Request is not null, () =>
        {
            _ = RuleFor(x => x.Request.WorkflowId)
                .NotEmpty().WithMessage("Workflow ID is required.")
                .MaximumLength(100);

            // Validate individual files (if present)
            _ = RuleForEach(x => x.Request.Files)
                .SetValidator(new PostDocumentInfoRequestValidator());

            // CounterpartyId must not be among PeerCompanyIds
            _ = RuleFor(x => x.Request.CounterpartyId)
                .Must((root, counterpartyId) =>
                    string.IsNullOrEmpty(counterpartyId) ||
                    root.Request.PeerCompanyIds is null ||
                    !root.Request.PeerCompanyIds.Contains(counterpartyId))
                .WithMessage("CounterpartyId cannot also be in the list of peer companies.");

            // Maximum of 3 peer companies
            _ = RuleFor(x => x.Request.PeerCompanyIds)
                .Must(list => list is null || list.Count <= 3)
                .WithMessage("A maximum of 3 peer companies is allowed.");

            _ = RuleFor(x => x.Request.AttachmentsDocumentClass)
                .MaximumLength(100);

            _ = RuleFor(x => x.Request.FreezeSourceExecutionId)
                .MaximumLength(450);
        });
    }
}

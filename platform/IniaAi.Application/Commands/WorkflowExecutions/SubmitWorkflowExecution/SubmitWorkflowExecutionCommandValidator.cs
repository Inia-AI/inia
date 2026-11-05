using FluentValidation;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.WebApi.Contracts.Requests.WorkflowExecutions;
using Microsoft.AspNetCore.Http;

namespace IniaAi.Application.Commands.WorkflowExecutions.SubmitWorkflowExecution;

/// <summary>
/// Validates the <see cref="SubmitWorkflowExecutionCommand"/> before processing.
/// Ensures required fields are present, files are provided, and metadata is consistent.
/// </summary>
public class SubmitWorkflowExecutionCommandValidator : AbstractValidator<SubmitWorkflowExecutionCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SubmitWorkflowExecutionCommandValidator"/> class.
    /// </summary>
    public SubmitWorkflowExecutionCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId)
            .NotEmpty().WithMessage("Organization ID must be provided.");

        _ = RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User ID must be provided.");

        _ = RuleFor(x => x.Metadata)
            .NotNull().WithMessage("Metadata must be provided.");

        _ = RuleFor(x => x.Files)
            .NotEmpty().WithMessage("At least one file must be provided.");

        _ = RuleFor(x => x.IdempotencyKey)
            .MaximumLength(WorkflowExecution.IDEMPOTENCY_KEY_MAX_LENGTH)
            .When(x => x.IdempotencyKey is not null);

        _ = When(x => x.Metadata is not null, () =>
        {
            _ = RuleFor(x => x.Metadata.WorkflowTypeId)
                .NotEmpty().WithMessage("Workflow ID is required.")
                .MaximumLength(100);

            _ = RuleFor(x => x.Metadata.FreezeSourceExecutionId)
                .MaximumLength(450);

            _ = RuleFor(x => x.Metadata.FreezeSourceCaseId)
                .MaximumLength(450)
                .Must((command, freezeSourceCaseId) =>
                    string.IsNullOrEmpty(freezeSourceCaseId) || string.IsNullOrEmpty(command.Metadata.FreezeSourceExecutionId))
                .WithMessage("Name a freeze source execution or a freeze source case, not both.");

            // Metadata file entries must not exceed actual file count
            _ = RuleFor(x => x.Files)
                .Must((command, files) =>
                    command.Metadata.Files is null ||
                    files is null ||
                    command.Metadata.Files.Count <= files.Count)
                .WithMessage("Metadata file entries cannot exceed the number of uploaded files.");

            _ = RuleForEach(x => x.Metadata.Files).ChildRules(file =>
            {
                _ = file.RuleFor(f => f.DocumentClass).MaximumLength(100);
                _ = file.RuleFor(f => f.AttachmentsDocumentClass).MaximumLength(100);
            });

            // CounterpartyId must not be among PeerCompanyIds
            _ = RuleFor(x => x.Metadata.CounterpartyId)
                .Must((command, counterpartyId) =>
                    string.IsNullOrEmpty(counterpartyId) ||
                    command.Metadata.PeerCompanyIds is null ||
                    !command.Metadata.PeerCompanyIds.Contains(counterpartyId))
                .WithMessage("CounterpartyId cannot also be in the list of peer companies.");

            // Maximum of 3 peer companies
            _ = RuleFor(x => x.Metadata.PeerCompanyIds)
                .Must(list => list is null || list.Count <= 3)
                .WithMessage("A maximum of 3 peer companies is allowed.");
        });
    }
}

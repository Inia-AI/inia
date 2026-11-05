using FluentValidation;

namespace IniaAi.Application.Queries.Workflows.ValidateDocumentClasses;

public class ValidateDocumentClassesQueryValidator : AbstractValidator<ValidateDocumentClassesQuery>
{
    public ValidateDocumentClassesQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId)
            .NotEmpty()
            .WithMessage("OrganizationId is required");

        _ = RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("UserId is required");

        _ = RuleFor(x => x.WorkflowId)
            .NotEmpty()
            .WithMessage("WorkflowId is required");

        _ = RuleFor(x => x.DocumentIds)
            .NotNull()
            .WithMessage("DocumentIds list is required")
            .Must(list => list is not null && list.Count > 0)
            .WithMessage("At least one document ID must be provided");
    }
}

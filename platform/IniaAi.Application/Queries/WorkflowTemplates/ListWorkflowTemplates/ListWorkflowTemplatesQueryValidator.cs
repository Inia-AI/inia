using FluentValidation;

namespace IniaAi.Application.Queries.WorkflowTemplates.ListWorkflowTemplates;

/// <summary>
/// Validates <see cref="ListWorkflowTemplatesQuery"/>.
/// </summary>
public class ListWorkflowTemplatesQueryValidator : AbstractValidator<ListWorkflowTemplatesQuery>
{
    public ListWorkflowTemplatesQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
    }
}

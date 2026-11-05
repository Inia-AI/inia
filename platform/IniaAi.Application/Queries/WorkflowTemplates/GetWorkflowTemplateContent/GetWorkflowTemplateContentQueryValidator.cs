using FluentValidation;

namespace IniaAi.Application.Queries.WorkflowTemplates.GetWorkflowTemplateContent;

/// <summary>
/// Validates <see cref="GetWorkflowTemplateContentQuery"/>.
/// </summary>
public class GetWorkflowTemplateContentQueryValidator : AbstractValidator<GetWorkflowTemplateContentQuery>
{
    public GetWorkflowTemplateContentQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.WorkflowTypeId).NotEmpty();
    }
}

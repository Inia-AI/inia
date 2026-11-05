using FluentValidation;

namespace IniaAi.Application.Queries.WorkflowTemplates.GetWorkflowTemplateDocumentClasses;

public class GetWorkflowTemplateDocumentClassesQueryValidator
    : AbstractValidator<GetWorkflowTemplateDocumentClassesQuery>
{
    public GetWorkflowTemplateDocumentClassesQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.WorkflowTypeId).NotEmpty();
    }
}

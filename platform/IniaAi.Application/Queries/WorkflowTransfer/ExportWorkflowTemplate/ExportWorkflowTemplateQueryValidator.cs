using FluentValidation;

namespace IniaAi.Application.Queries.WorkflowTransfer.ExportWorkflowTemplate;

public class ExportWorkflowTemplateQueryValidator : AbstractValidator<ExportWorkflowTemplateQuery>
{
    public ExportWorkflowTemplateQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.WorkflowTypeId).NotEmpty();
    }
}

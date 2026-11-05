using FluentValidation;

namespace IniaAi.Application.Queries.Workflows.ListWorkflowCategories;

/// <summary>
/// Validates <see cref="ListWorkflowCategorySummariesQuery"/>.
/// </summary>
public class ListWorkflowCategorySummariesQueryValidator : AbstractValidator<ListWorkflowCategorySummariesQuery>
{
    public ListWorkflowCategorySummariesQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
    }
}

using FluentValidation;

namespace IniaAi.Application.Queries.WorkflowExecutions.GetReRunInputs;

/// <summary>
/// Validates <see cref="GetReRunInputsQuery"/>.
/// </summary>
public class GetReRunInputsQueryValidator : AbstractValidator<GetReRunInputsQuery>
{
    public GetReRunInputsQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.WorkflowExecutionId).NotEmpty();
    }
}

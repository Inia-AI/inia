using FluentValidation;

namespace IniaAi.Application.Queries.Counterparties.ListPeerCandidates;

/// <summary>
/// Validator for <see cref="ListPeerCandidatesQuery"/>.
/// </summary>
public class ListPeerCandidatesQueryValidator : AbstractValidator<ListPeerCandidatesQuery>
{
    private const int MAX_WORKFLOW_TYPE_ID_LENGTH = 100;
    private const int MAX_SEARCH_LENGTH = 250;

    public ListPeerCandidatesQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId)
            .NotEmpty().WithMessage("Organization ID must be provided.");

        _ = RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User ID must be provided.");

        _ = RuleFor(x => x.WorkflowTypeId)
            .MaximumLength(MAX_WORKFLOW_TYPE_ID_LENGTH)
            .When(x => !string.IsNullOrEmpty(x.WorkflowTypeId));

        _ = RuleFor(x => x.Search)
            .MaximumLength(MAX_SEARCH_LENGTH)
            .When(x => !string.IsNullOrEmpty(x.Search));

        _ = RuleFor(x => x.Skip)
            .GreaterThanOrEqualTo(0);

        _ = RuleFor(x => x.Top)
            .GreaterThan(0)
            .LessThanOrEqualTo(ListPeerCandidatesQuery.MAX_TOP);
    }
}

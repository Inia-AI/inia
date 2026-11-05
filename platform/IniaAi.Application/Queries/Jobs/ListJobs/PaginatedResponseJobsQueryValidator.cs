using FluentValidation;

namespace IniaAi.Application.Queries.Jobs.ListJobs;

/// <summary>
/// Validator for the <see cref="PaginatedResponseJobsQuery"/>.
/// </summary>
public class PaginatedResponseJobsQueryValidator : AbstractValidator<PaginatedResponseJobsQuery>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PaginatedResponseJobsQueryValidator"/> class.
    /// </summary>
    public PaginatedResponseJobsQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId)
            .NotEmpty()
            .WithMessage("Organization ID is required");

        _ = RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required");
    }
}

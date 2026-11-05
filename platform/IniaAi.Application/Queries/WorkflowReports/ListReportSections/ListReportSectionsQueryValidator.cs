using FluentValidation;

namespace IniaAi.Application.Queries.WorkflowReports.ListReportSections;

/// <summary>
/// Validator for <see cref="ListReportSectionsQuery"/>.
/// </summary>
public class ListReportSectionsQueryValidator : AbstractValidator<ListReportSectionsQuery>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ListReportSectionsQueryValidator"/> class.
    /// </summary>
    public ListReportSectionsQueryValidator()
    {
        _ = RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");

        _ = RuleFor(x => x.OrganizationId)
            .NotEmpty()
            .WithMessage("OrganizationId is required.");
    }
}

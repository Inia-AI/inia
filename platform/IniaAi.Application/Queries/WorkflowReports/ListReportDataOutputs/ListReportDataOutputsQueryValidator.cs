using FluentValidation;

namespace IniaAi.Application.Queries.WorkflowReports.ListReportDataOutputs;

/// <summary>
/// Validator for <see cref="ListReportDataOutputsQuery"/>.
/// </summary>
public class ListReportDataOutputsQueryValidator : AbstractValidator<ListReportDataOutputsQuery>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ListReportDataOutputsQueryValidator"/> class.
    /// </summary>
    public ListReportDataOutputsQueryValidator()
    {
        _ = RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");

        _ = RuleFor(x => x.OrganizationId)
            .NotEmpty()
            .WithMessage("OrganizationId is required.");
    }
}

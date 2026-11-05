using FluentValidation.Results;
using IniaAi.Application.Commands.ReportFeedback.CreateReportFeedback;
using IniaAi.WebApi.Contracts.Enums;
using IniaAi.WebApi.Contracts.Requests.ReportFeedback;

namespace IniaAi.Application.UnitTests.Commands.ReportFeedback;

public class CreateReportFeedbackCommandValidatorTests
{
    private readonly CreateReportFeedbackCommandValidator _validator = new();

    [Fact]
    public void NullRequest_IsAValidationError_NotAnException()
    {
        ValidationResult result = _validator.Validate(new CreateReportFeedbackCommand("org", "user", "exec", null!));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateReportFeedbackCommand.Request));
    }

    [Fact]
    public void EmptyComment_IsRefused()
    {
        ValidationResult result = _validator.Validate(new CreateReportFeedbackCommand("org", "user", "exec", new CreateReportFeedbackRequest
        {
            Category = ReportFeedbackCategoryDto.NotRelevant,
            Comment = "  ",
        }));

        Assert.False(result.IsValid);
    }
}

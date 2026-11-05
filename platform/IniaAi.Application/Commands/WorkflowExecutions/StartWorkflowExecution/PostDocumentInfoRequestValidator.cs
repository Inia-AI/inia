using FluentValidation;
using IniaAi.WebApi.Contracts.Requests;

namespace IniaAi.Application.Commands.WorkflowExecutions.StartWorkflowExecution;

/// <summary>
/// Validator for individual document file metadata in workflow execution.
/// </summary>
public class PostDocumentInfoRequestValidator : AbstractValidator<PostDocumentInfoRequest>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PostDocumentInfoRequestValidator"/> class.
    /// </summary>
    public PostDocumentInfoRequestValidator()
    {
        _ = RuleFor(x => x.FileId)
            .NotEmpty().WithMessage("FileId is required.");

        _ = RuleFor(x => x.DocumentClass)
            .MaximumLength(100);
    }
}

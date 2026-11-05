using FluentValidation;

namespace IniaAi.Application.Commands.Files.UploadFile;

/// <summary>
/// Validates the <see cref="UploadFileCommand"/> before processing: the caller, a file, and a
/// password within bounds. What the file is, whether it is empty, and whether it fits the
/// deployment's size limit are decided by <see cref="Documents.DocumentIntakeGate"/> inside the
/// handler, because a rejection raised here runs before the handler and could never write its
/// screening record.
/// </summary>
public class UploadFileCommandValidator : AbstractValidator<UploadFileCommand>
{
    private const int MAX_PASSWORD_LENGTH = 256;

    public UploadFileCommandValidator()
    {
        _ = RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required.");

        _ = RuleFor(x => x.OrganizationId)
            .NotEmpty()
            .WithMessage("Organization ID is required.");

        _ = RuleFor(x => x.File)
            .NotNull().WithMessage("A file must be provided.");

        _ = RuleFor(x => x.FilePassword)
            .MaximumLength(MAX_PASSWORD_LENGTH)
            .WithMessage($"File password must not exceed {MAX_PASSWORD_LENGTH} characters.");
    }
}

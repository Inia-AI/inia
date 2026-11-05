using FluentValidation;

namespace IniaAi.Application.Commands.Authentication.SendEmailOtp;

/// <summary>
/// Validator for the <see cref="SendEmailOtpCommand"/>.
/// </summary>
public class SendEmailOtpCommandValidator : AbstractValidator<SendEmailOtpCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SendEmailOtpCommandValidator"/> class.
    /// </summary>
    public SendEmailOtpCommandValidator()
    {
        _ = RuleFor(x => x.MfaToken)
            .NotEmpty()
            .WithMessage("MFA token is required");
    }
}

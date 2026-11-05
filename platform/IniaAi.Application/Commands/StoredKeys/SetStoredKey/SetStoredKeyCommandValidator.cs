using FluentValidation;
using IniaAi.Domain.StoredKeys;

namespace IniaAi.Application.Commands.StoredKeys.SetStoredKey;

public class SetStoredKeyCommandValidator : AbstractValidator<SetStoredKeyCommand>
{
    public SetStoredKeyCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.Name).Must(StoredKeyNames.IsValid).WithMessage(StoredKeyNames.RULE);
        _ = RuleFor(x => x.Value)
            .NotEmpty()
            .MaximumLength(StoredKeyNames.VALUE_MAX_LENGTH)
            .Must(value => value.Trim().IndexOfAny(['\r', '\n']) < 0)
            .WithMessage("A key cannot hold a line break.");
    }
}

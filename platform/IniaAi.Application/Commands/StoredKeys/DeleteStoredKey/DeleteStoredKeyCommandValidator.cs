using FluentValidation;
using IniaAi.Domain.StoredKeys;

namespace IniaAi.Application.Commands.StoredKeys.DeleteStoredKey;

public class DeleteStoredKeyCommandValidator : AbstractValidator<DeleteStoredKeyCommand>
{
    public DeleteStoredKeyCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.Name).NotEmpty().MaximumLength(StoredKeyNames.MAX_LENGTH);
    }
}

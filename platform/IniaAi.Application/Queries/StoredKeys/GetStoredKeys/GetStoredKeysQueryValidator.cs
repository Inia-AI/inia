using FluentValidation;

namespace IniaAi.Application.Queries.StoredKeys.GetStoredKeys;

public class GetStoredKeysQueryValidator : AbstractValidator<GetStoredKeysQuery>
{
    public GetStoredKeysQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
    }
}

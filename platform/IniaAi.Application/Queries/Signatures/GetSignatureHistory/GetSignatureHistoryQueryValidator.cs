using FluentValidation;

namespace IniaAi.Application.Queries.Signatures.GetSignatureHistory;

public class GetSignatureHistoryQueryValidator : AbstractValidator<GetSignatureHistoryQuery>
{
    private const int MAX_TAKE = 200;

    public GetSignatureHistoryQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.WorkflowTypeId).NotEmpty().MaximumLength(200);
        _ = RuleFor(x => x.PlaceholderName).MaximumLength(200)
            .When(x => !string.IsNullOrWhiteSpace(x.PlaceholderName));
        _ = RuleFor(x => x.ActorUserId).MaximumLength(450)
            .When(x => !string.IsNullOrWhiteSpace(x.ActorUserId));
        _ = RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        _ = RuleFor(x => x.Take).InclusiveBetween(1, MAX_TAKE);
        // Cross-field rule attached to ToUtc so the validation failure surfaces on
        // a named property in the response envelope. Attaching to RuleFor(x => x)
        // would key the error to the empty string which most error envelopes
        // strip, hiding it from the client.
        _ = RuleFor(x => x.ToUtc)
            .Must((query, _) => query.FromUtc!.Value <= query.ToUtc!.Value)
            .When(x => x.FromUtc.HasValue && x.ToUtc.HasValue)
            .WithMessage("ToUtc must be greater than or equal to FromUtc.");
    }
}

using FluentValidation;

namespace IniaAi.Application.Queries.Documents.GetDocumentPage;

public class GetDocumentPageQueryValidator : AbstractValidator<GetDocumentPageQuery>
{
    public GetDocumentPageQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.DocumentId).NotEmpty().MaximumLength(450);
        _ = RuleFor(x => x.PageNumber).GreaterThan(0);
    }
}

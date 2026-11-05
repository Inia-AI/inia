using ErrorOr;
using FluentValidation;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Specifications.SharedComponents;
using IniaAi.WebApi.Contracts.Responses.SharedComponents;

namespace IniaAi.Application.Queries.SharedComponents.ListSharedComponentCategories;

/// <summary>
/// Lists the Library's curated category vocabulary in display order, with per-category component
/// counts and the count of uncategorized components. Counts cover every registry row regardless of
/// lifecycle state — the catalogue is a registry view, so retired components still count toward
/// their category. The vocabulary is one global pool per deployment: the caller identity is
/// validated but never used to scope the result; access control is the endpoint's role gate.
/// </summary>
public record ListSharedComponentCategoriesQuery(
    string OrganizationId,
    string UserId) : IRequest<ErrorOr<SharedComponentCategoriesDto>>;

public class ListSharedComponentCategoriesQueryHandler
    : IRequestHandler<ListSharedComponentCategoriesQuery, ErrorOr<SharedComponentCategoriesDto>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;

    public ListSharedComponentCategoriesQueryHandler(ICoreUnitOfWork coreUnitOfWork)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
    }

    public async Task<ErrorOr<SharedComponentCategoriesDto>> Handle(
        ListSharedComponentCategoriesQuery request,
        CancellationToken cancellationToken)
    {
        List<SharedComponentCategory> categories = [.. await _coreUnitOfWork
            .Repository<SharedComponentCategory>()
            .FindAsync(new SharedComponentCategoriesSpec(), cancellationToken)];
        List<SharedComponent> components = [.. await _coreUnitOfWork
            .Repository<SharedComponent>()
            .FindAsync(new SharedComponentsListSpec(), cancellationToken)];

        Dictionary<string, int> countsByCategory = components
            .Where(c => c.CategoryId is not null)
            .GroupBy(c => c.CategoryId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        return new SharedComponentCategoriesDto(
            categories
                .Select(c => new SharedComponentCategoryDto(
                    c.Id,
                    c.Name,
                    c.Description,
                    c.DisplayOrder,
                    countsByCategory.GetValueOrDefault(c.Id)))
                .ToList(),
            components.Count(c => c.CategoryId is null));
    }
}

public class ListSharedComponentCategoriesQueryValidator : AbstractValidator<ListSharedComponentCategoriesQuery>
{
    public ListSharedComponentCategoriesQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
    }
}

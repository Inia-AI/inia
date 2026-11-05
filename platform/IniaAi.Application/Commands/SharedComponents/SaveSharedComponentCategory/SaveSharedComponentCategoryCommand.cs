using ErrorOr;
using FluentValidation;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.WebApi.Contracts.Responses.SharedComponents;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Commands.SharedComponents.SaveSharedComponentCategory;

/// <summary>
/// Creates or updates one entry of the Library's curated category vocabulary. The id is a stable
/// kebab-case id chosen at creation and never changed afterwards — renames touch only the display
/// name. Saving over a soft-deleted id resurrects the row (the primary key stays occupied by
/// soft delete, so recreation must revive rather than insert).
/// </summary>
public record SaveSharedComponentCategoryCommand(
    string OrganizationId,
    string UserId,
    string CategoryId,
    string Name,
    string? Description = null,
    int? DisplayOrder = null) : IRequest<ErrorOr<SharedComponentCategoryDto>>;

public class SaveSharedComponentCategoryCommandHandler
    : IRequestHandler<SaveSharedComponentCategoryCommand, ErrorOr<SharedComponentCategoryDto>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;

    public SaveSharedComponentCategoryCommandHandler(ICoreUnitOfWork coreUnitOfWork)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
    }

    public async Task<ErrorOr<SharedComponentCategoryDto>> Handle(
        SaveSharedComponentCategoryCommand request,
        CancellationToken cancellationToken)
    {
        SharedComponentCategory category = await SharedComponentCategoryGuard.CreateOrReviveAsync(
            _coreUnitOfWork,
            request.CategoryId,
            request.Name,
            request.Description,
            request.DisplayOrder,
            overwriteVisible: true,
            cancellationToken);
        _ = await _coreUnitOfWork.CompleteAsync(cancellationToken);

        int componentCount = await _coreUnitOfWork.Repository<SharedComponent>()
            .GetAll()
            .AsNoTracking()
            .CountAsync(c => c.CategoryId == request.CategoryId, cancellationToken);

        return new SharedComponentCategoryDto(
            category.Id,
            category.Name,
            category.Description,
            category.DisplayOrder,
            componentCount);
    }
}

public class SaveSharedComponentCategoryCommandValidator : AbstractValidator<SaveSharedComponentCategoryCommand>
{
    private const string CATEGORY_ID_PATTERN = "^[a-z0-9]+(-[a-z0-9]+)*$";

    public SaveSharedComponentCategoryCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.CategoryId)
            .NotEmpty()
            .MaximumLength(100)
            .Matches(CATEGORY_ID_PATTERN)
            .WithMessage("CategoryId must be a kebab-case id (lowercase letters, digits, single dashes).");
        _ = RuleFor(x => x.Name).NotEmpty().MaximumLength(SharedComponentCategory.NAME_MAX_LENGTH);
        _ = RuleFor(x => x.Description).MaximumLength(SharedComponentCategory.DESCRIPTION_MAX_LENGTH);
    }
}

using ErrorOr;
using FluentValidation;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Commands.SharedComponents.DeleteSharedComponentCategory;

/// <summary>
/// Removes one entry of the Library's curated category vocabulary. Refused while any registry
/// component — retired ones included — still references the entry: components reference categories
/// by plain string with no foreign key, so a deleted-but-referenced category would silently
/// degrade every carrier to an unresolvable id. Re-categorize the components first.
/// </summary>
public record DeleteSharedComponentCategoryCommand(
    string OrganizationId,
    string UserId,
    string CategoryId) : IRequest<ErrorOr<Deleted>>;

public class DeleteSharedComponentCategoryCommandHandler
    : IRequestHandler<DeleteSharedComponentCategoryCommand, ErrorOr<Deleted>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;

    public DeleteSharedComponentCategoryCommandHandler(ICoreUnitOfWork coreUnitOfWork)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
    }

    public async Task<ErrorOr<Deleted>> Handle(
        DeleteSharedComponentCategoryCommand request,
        CancellationToken cancellationToken)
    {
        SharedComponentCategory? category = await _coreUnitOfWork.Repository<SharedComponentCategory>()
            .GetAll()
            .FirstOrDefaultAsync(c => c.Id == request.CategoryId, cancellationToken);
        if (category is null)
        {
            return Error.NotFound(
                SharedComponentErrorCodes.UNKNOWN_CATEGORY,
                $"No category with id '{request.CategoryId}' exists.");
        }

        int referenceCount = await _coreUnitOfWork.Repository<SharedComponent>()
            .GetAll()
            .AsNoTracking()
            .CountAsync(c => c.CategoryId == request.CategoryId, cancellationToken);
        if (referenceCount > 0)
        {
            return Error.Conflict(
                SharedComponentErrorCodes.CATEGORY_IN_USE,
                $"Category '{request.CategoryId}' is referenced by {referenceCount} component(s); re-categorize them first.");
        }

        _coreUnitOfWork.Repository<SharedComponentCategory>().Remove(category);
        _ = await _coreUnitOfWork.CompleteAsync(cancellationToken);

        return Result.Deleted;
    }
}

public class DeleteSharedComponentCategoryCommandValidator : AbstractValidator<DeleteSharedComponentCategoryCommand>
{
    public DeleteSharedComponentCategoryCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.CategoryId).NotEmpty();
    }
}

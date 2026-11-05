using ErrorOr;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Common.Workflows;

/// <summary>
/// The single point through which the curated category vocabulary is read and written. Categories
/// carry no foreign key (registry no-FK style), so <see cref="EnsureExistsAsync"/> is the write-mouth
/// vocabulary check every CategoryId stamp validates through, and <see cref="CreateOrReviveAsync"/>
/// is the one place a category row comes into existence — creating it when absent and reviving it
/// when a soft-deleted row already occupies the id (the primary key stays occupied by soft delete,
/// so recreation must revive rather than insert).
/// </summary>
internal static class SharedComponentCategoryGuard
{
    internal static async Task<ErrorOr<Success>> EnsureExistsAsync(
        ICoreUnitOfWork coreUnitOfWork,
        string categoryId,
        CancellationToken cancellationToken)
    {
        bool categoryExists = await coreUnitOfWork.Repository<SharedComponentCategory>()
            .GetAll()
            .AsNoTracking()
            .AnyAsync(c => c.Id == categoryId, cancellationToken);
        if (!categoryExists)
        {
            return Error.NotFound(
                SharedComponentErrorCodes.UNKNOWN_CATEGORY,
                $"No category with id '{categoryId}' exists; list the vocabulary to pick a valid one.");
        }

        return Result.Success;
    }

    internal static async Task<SharedComponentCategory> CreateOrReviveAsync(
        ICoreUnitOfWork coreUnitOfWork,
        string categoryId,
        string name,
        string? description,
        int? displayOrder,
        bool overwriteVisible,
        CancellationToken cancellationToken)
    {
        SharedComponentCategory? category = await coreUnitOfWork.Repository<SharedComponentCategory>()
            .GetAll()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == categoryId, cancellationToken);

        if (category is null)
        {
            category = new SharedComponentCategory
            {
                Id = categoryId,
                Name = name,
                Description = description,
                DisplayOrder = displayOrder,
            };
            await coreUnitOfWork.Repository<SharedComponentCategory>().AddAsync(category);

            return category;
        }

        if (category.IsDeleted || overwriteVisible)
        {
            category.Name = name;
            category.Description = description;
            category.DisplayOrder = displayOrder;
            category.IsDeleted = false;
            category.DeletedAt = null;
            category.DeletedByUserId = null;
        }

        return category;
    }
}

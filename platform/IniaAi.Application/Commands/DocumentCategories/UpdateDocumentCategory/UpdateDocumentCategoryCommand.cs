using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.DocumentCategories;

namespace IniaAi.Application.Commands.DocumentCategories.UpdateDocumentCategory;

/// <summary>
/// Command to update an existing document category.
/// </summary>
/// <param name="OrganizationId">The organization ID making the request (SuperAdmin context).</param>
/// <param name="UserId">The user ID making the request.</param>
/// <param name="Id">The ID of the document category to update.</param>
/// <param name="Name">The new name for the document category.</param>
/// <param name="DisplayOrder">The new display order for the category.</param>
public record UpdateDocumentCategoryCommand(
    string OrganizationId,
    string UserId,
    string Id,
    string? Name,
    int? DisplayOrder) : IRequest<ErrorOr<DocumentCategoryDto>>;

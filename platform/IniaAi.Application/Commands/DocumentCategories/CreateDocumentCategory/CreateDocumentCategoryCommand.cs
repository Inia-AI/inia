using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.DocumentCategories;

namespace IniaAi.Application.Commands.DocumentCategories.CreateDocumentCategory;

/// <summary>
/// Command to create a new document category.
/// </summary>
/// <param name="OrganizationId">The organization ID making the request (SuperAdmin context).</param>
/// <param name="UserId">The user ID making the request.</param>
/// <param name="Name">The name of the document category.</param>
/// <param name="DisplayOrder">The display order for sorting categories.</param>
public record CreateDocumentCategoryCommand(
    string OrganizationId,
    string UserId,
    string Name,
    int? DisplayOrder) : IRequest<ErrorOr<DocumentCategoryDto>>;

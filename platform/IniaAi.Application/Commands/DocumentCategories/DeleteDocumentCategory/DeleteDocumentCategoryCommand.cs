using ErrorOr;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Commands.DocumentCategories.DeleteDocumentCategory;

/// <summary>
/// Command to delete a document category.
/// </summary>
/// <param name="OrganizationId">The organization ID making the request (SuperAdmin context).</param>
/// <param name="UserId">The user ID making the request.</param>
/// <param name="Id">The ID of the document category to delete.</param>
public record DeleteDocumentCategoryCommand(
    string OrganizationId,
    string UserId,
    string Id) : IRequest<ErrorOr<Deleted>>;

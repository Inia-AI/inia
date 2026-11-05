using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.DocumentCategories;

namespace IniaAi.Application.Queries.DocumentCategories.ListDocumentCategories;

/// <summary>
/// Represents a query to retrieve all document categories.
/// </summary>
public record ListDocumentCategoriesQuery : IRequest<ErrorOr<List<DocumentCategoryDto>>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ListDocumentCategoriesQuery"/> class.
    /// </summary>
    /// <param name="organizationId">The organization ID making the request.</param>
    /// <param name="userId">The user ID making the request.</param>
    public ListDocumentCategoriesQuery(string organizationId, string userId)
    {
        OrganizationId = organizationId;
        UserId = userId;
    }

    /// <summary>
    /// Gets the organization ID making the request.
    /// </summary>
    public string OrganizationId { get; }

    /// <summary>
    /// Gets the user ID making the request.
    /// </summary>
    public string UserId { get; }
}

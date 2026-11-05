using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.Pages;

namespace IniaAi.Application.Queries.Pages.GetPageImage;

/// <summary>
/// Query to retrieve a page image by page ID.
/// </summary>
/// <param name="OrganizationId">The organization ID for tenant isolation.</param>
/// <param name="UserId">The user ID for audit logging.</param>
/// <param name="PageId">The unique identifier of the page.</param>
public sealed record GetPageImageQuery(
    string OrganizationId,
    string UserId,
    string PageId) : IRequest<ErrorOr<PageImageResultDto>>;

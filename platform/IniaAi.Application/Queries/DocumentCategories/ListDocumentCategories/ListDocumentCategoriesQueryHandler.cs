using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Mappers;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows.Templates;
using IniaAi.Domain.Specifications.DocumentCategories;
using IniaAi.WebApi.Contracts.Responses.DocumentCategories;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Queries.DocumentCategories.ListDocumentCategories;

/// <summary>
/// Handles the query to retrieve all document categories.
/// </summary>
public class ListDocumentCategoriesQueryHandler : IRequestHandler<ListDocumentCategoriesQuery, ErrorOr<List<DocumentCategoryDto>>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListDocumentCategoriesQueryHandler"/> class.
    /// </summary>
    /// <param name="coreUnitOfWork">The core unit of work.</param>
    public ListDocumentCategoriesQueryHandler(
        ICoreUnitOfWork coreUnitOfWork)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
    }

    /// <summary>
    /// Handles the query request to return all document categories.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of document category DTOs.</returns>
    public async Task<ErrorOr<List<DocumentCategoryDto>>> Handle(ListDocumentCategoriesQuery request, CancellationToken cancellationToken)
    {
        var spec = new DocumentCategoriesSpec();
        List<DocumentCategory> categories = await _coreUnitOfWork.Repository<DocumentCategory>()
            .GetAll(spec)
            .OrderBy(dc => dc.Name)
            .ToListAsync(cancellationToken);

        return categories.Select(DocumentCategoryDtoMapper.ToDto).ToList();
    }
}

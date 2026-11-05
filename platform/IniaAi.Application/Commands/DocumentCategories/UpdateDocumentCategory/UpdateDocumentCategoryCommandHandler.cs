using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Mappers;
using IniaAi.Common.Text;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows.Templates;
using IniaAi.Domain.Specifications.DocumentCategories;
using IniaAi.WebApi.Contracts.Responses.DocumentCategories;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.DocumentCategories.UpdateDocumentCategory;

/// <summary>
/// Handles the update of an existing document category.
/// </summary>
public class UpdateDocumentCategoryCommandHandler : IRequestHandler<UpdateDocumentCategoryCommand, ErrorOr<DocumentCategoryDto>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly ILogger<UpdateDocumentCategoryCommandHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateDocumentCategoryCommandHandler"/> class.
    /// </summary>
    /// <param name="coreUnitOfWork">The core unit of work.</param>
    /// <param name="logger">The logger instance.</param>
    public UpdateDocumentCategoryCommandHandler(
        ICoreUnitOfWork coreUnitOfWork,
        ILogger<UpdateDocumentCategoryCommandHandler> logger)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Handles the command to update an existing document category.
    /// </summary>
    /// <param name="request">The update document category command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated document category DTO or an error.</returns>
    public async Task<ErrorOr<DocumentCategoryDto>> Handle(UpdateDocumentCategoryCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Updating document category with ID: {Id} - SuperAdmin: {UserId}",
            LogSanitizer.ForLog(request.Id),
            request.UserId);

        // Find the existing category with tracking enabled
        var spec = new DocumentCategoryByIdTrackingSpec(request.Id);
        DocumentCategory? category = await _coreUnitOfWork.Repository<DocumentCategory>()
            .GetFirstOrDefaultAsync(spec, cancellationToken);

        if (category is null)
        {
            _logger.LogWarning("Document category with ID {Id} not found", LogSanitizer.ForLog(request.Id));
            return Error.NotFound("DocumentCategory.NotFound", $"Document category with ID '{request.Id}' was not found");
        }

        // Update fields if provided
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            category.Name = request.Name;
        }

        if (request.DisplayOrder.HasValue)
        {
            category.DisplayOrder = request.DisplayOrder.Value;
        }

        _ = await _coreUnitOfWork.CompleteAsync(cancellationToken);

        _logger.LogInformation("Successfully updated document category: {Id}", LogSanitizer.ForLog(request.Id));

        return DocumentCategoryDtoMapper.ToDto(category);
    }
}

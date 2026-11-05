using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Common.Text;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows.Templates;
using IniaAi.Domain.Specifications.DocumentCategories;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.DocumentCategories.DeleteDocumentCategory;

/// <summary>
/// Handles the deletion of a document category.
/// </summary>
public class DeleteDocumentCategoryCommandHandler : IRequestHandler<DeleteDocumentCategoryCommand, ErrorOr<Deleted>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly ILogger<DeleteDocumentCategoryCommandHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeleteDocumentCategoryCommandHandler"/> class.
    /// </summary>
    /// <param name="coreUnitOfWork">The core unit of work.</param>
    /// <param name="logger">The logger instance.</param>
    public DeleteDocumentCategoryCommandHandler(
        ICoreUnitOfWork coreUnitOfWork,
        ILogger<DeleteDocumentCategoryCommandHandler> logger)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Handles the command to delete a document category.
    /// </summary>
    /// <param name="request">The delete document category command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A Deleted result or an error.</returns>
    public async Task<ErrorOr<Deleted>> Handle(DeleteDocumentCategoryCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Deleting document category with ID: {Id} - SuperAdmin: {UserId}",
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

        // Remove the category
        _coreUnitOfWork.Repository<DocumentCategory>().Remove(category);
        _ = await _coreUnitOfWork.CompleteAsync(cancellationToken);

        _logger.LogInformation("Successfully deleted document category: {Id}", LogSanitizer.ForLog(request.Id));

        return Result.Deleted;
    }
}

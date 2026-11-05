using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Mappers;
using IniaAi.Common.Text;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows.Templates;
using IniaAi.Domain.Specifications.DocumentCategories;
using IniaAi.WebApi.Contracts.Responses.DocumentCategories;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.DocumentCategories.CreateDocumentCategory;

/// <summary>
/// Handles the creation of a new document category.
/// </summary>
public class CreateDocumentCategoryCommandHandler : IRequestHandler<CreateDocumentCategoryCommand, ErrorOr<DocumentCategoryDto>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly ILogger<CreateDocumentCategoryCommandHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateDocumentCategoryCommandHandler"/> class.
    /// </summary>
    /// <param name="coreUnitOfWork">The core unit of work.</param>
    /// <param name="logger">The logger instance.</param>
    public CreateDocumentCategoryCommandHandler(
        ICoreUnitOfWork coreUnitOfWork,
        ILogger<CreateDocumentCategoryCommandHandler> logger)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Handles the command to create a new document category.
    /// </summary>
    /// <param name="request">The create document category command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created document category DTO or an error.</returns>
    public async Task<ErrorOr<DocumentCategoryDto>> Handle(CreateDocumentCategoryCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Creating document category with Name: {Name} - SuperAdmin: {UserId}",
            LogSanitizer.ForLog(request.Name),
            request.UserId);

        // Create the category using the factory method (auto-generates kebab-case ID)
        var category = DocumentCategory.Create(
            request.Name,
            request.UserId,
            request.DisplayOrder ?? 0);

        // Check if category with this ID already exists
        var existingSpec = new DocumentCategoryByIdSpec(category.Id);
        DocumentCategory? existingCategory = await _coreUnitOfWork.Repository<DocumentCategory>()
            .GetFirstOrDefaultAsync(existingSpec, cancellationToken);

        if (existingCategory is not null)
        {
            _logger.LogWarning("Document category with ID {Id} already exists", category.Id);
            return Error.Conflict("DocumentCategory.AlreadyExists", $"Document category with name '{request.Name}' already exists");
        }

        await _coreUnitOfWork.Repository<DocumentCategory>().AddAsync(category);
        _ = await _coreUnitOfWork.CompleteAsync(cancellationToken);

        _logger.LogInformation("Successfully created document category: {Id}", category.Id);

        return DocumentCategoryDtoMapper.ToDto(category);
    }
}

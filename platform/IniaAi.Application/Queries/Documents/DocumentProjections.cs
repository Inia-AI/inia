using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Specifications.Common;
using IniaAi.WebApi.Contracts.Responses.Documents;
using System.Linq.Expressions;
using ContractProcessingState = IniaAi.WebApi.Contracts.Enums.DocumentProcessingState;

namespace IniaAi.Application.Queries.Documents;

public static class DocumentProjections
{
    public static readonly Expression<Func<Document, DocumentSummaryDto>> ToSummary = ProjectionExpressions.Of<Document, DocumentSummaryDto>(document => new DocumentSummaryDto
    {
        Id = document.Id,
        Name = document.Name,
        MediaType = document.MediaType.ToString(),
        UserId = document.UserId,
        DocumentCategoryId = document.DocumentCategoryId,
        WorkflowTypeId = document.WorkflowTypeId,
        Class = document.Class,
        WasPasswordProtected = document.WasPasswordProtected,
        ProcessingState = (ContractProcessingState)DocumentProcessingStates.Of.Inline(document),
        ProcessingRequestedAt = document.ProcessingRequestedAt,
        ProcessedAt = document.ProcessedAt,
        ProcessingError = document.ProcessingError,
        CreatedAt = document.CreatedAt,
        UpdatedAt = document.UpdatedAt,
        SizeBytes = document.FileMetadata != null ? document.FileMetadata.SizeBytes : 0,
        Extension = document.FileMetadata != null ? document.FileMetadata.Extension : string.Empty,
    });

    public static readonly Expression<Func<Document, DocumentDetailDto>> ToDetail = ProjectionExpressions.Extend(ToSummary, document => new DocumentDetailDto
    {
        Id = document.Id,
        Description = document.Description,
        Sources = document.Sources,
        Topics = document.Topics,
        TableOfContents = document.TableOfContents,
        PageCount = document.Pages.Count,
        FileMetadataId = document.FileMetadataId,
    });

    private static readonly Func<Document, DocumentSummaryDto> _toSummary = ToSummary.Compile();

    public static DocumentSummaryDto SummaryOf(Document document)
    {
        return _toSummary(document);
    }
}

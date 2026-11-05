using IniaAi.Common;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Enums;
using IniaAi.Domain.Specifications.Common;
using IniaAi.WebApi.Contracts.Responses.Documents;
using System.Linq.Expressions;

namespace IniaAi.Application.Queries.Documents;

public static class PageProjections
{
    private const int TEXT_PREVIEW_LENGTH = 200;

    /// <summary>
    /// The text preview is cut inside the expression, so the database returns 200 characters
    /// rather than the whole page text of every listed page.
    /// </summary>
    public static readonly Expression<Func<Page, PageSummaryDto>> ToSummary = page => new PageSummaryDto
    {
        Id = page.Id,
        DocumentId = page.DocumentId,
        PageNumber = page.PageNumber,
        FilePageNumber = page.FilePageNumber,
        Overview = page.Overview,
        Location = page.Location.ToString(),
        Status = page.Status.ToString(),
        SourceUrl = page.SourceUrl,
        OcrNeedsReview = page.Status == PageStatus.OCRNeedsReview,
        TextPreview = string.IsNullOrEmpty(page.Text ?? page.RawText)
            ? null
            : (page.Text ?? page.RawText)!.Length <= TEXT_PREVIEW_LENGTH
                ? (page.Text ?? page.RawText)
                : (page.Text ?? page.RawText)!.Substring(0, TEXT_PREVIEW_LENGTH),
        HasEmbedding = page.EmbeddingVectorId != null,
        EmbeddingModel = page.EmbeddingVector != null ? page.EmbeddingVector.Model : null,
    };

    public static readonly Expression<Func<Page, PageDetailDto>> ToDetail = ProjectionExpressions.Extend(ToSummary, ProjectionExpressions.Of<Page, PageDetailDto>(page => new PageDetailDto
    {
        Id = page.Id,
        Text = page.Text ?? page.RawText,
        ReadableText = PageText.Readable.Inline(page),
        EmbeddingDimensions = page.EmbeddingVector != null ? page.EmbeddingVector.Vector.Length : null,
        SectionTitles = page.SectionTitles.NullIfWhiteSpace(),
    }));
}

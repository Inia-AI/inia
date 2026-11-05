using IniaAi.Common;
using IniaAi.Common.Content;
using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.Documents.Enums;
using IniaAi.Domain.Enums;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Documents;

/// <summary>
/// Represents a page in a document.
/// </summary>
public class Page
{
    private string? _rawText;
    private string? _text;

    [Key]
    public string Id { get; private set; } = Guid.NewGuid().ToString();
    public string DocumentId { get; set; } = string.Empty;
    public string? EmbeddingVectorId { get; set; }

    public Document? Document { get; set; }
    public EmbeddingVector? EmbeddingVector { get; set; }

    public int PageNumber { get; set; }

    public int FilePageNumber { get; set; }

    public string? SourceUrl
    {
        get;
        set => field = value.RemoveNullCharacters();
    }

    [DefaultValue(PageLocation.FullPage)]
    public PageLocation Location { get; set; } = PageLocation.FullPage;

    public string? RawText
    {
        get => _rawText ?? _text;
        set
        {
            _rawText = value.RemoveNullCharacters();
            _text ??= _rawText;
        }
    }

    public string? Text
    {
        get => _text ?? _rawText;
        set
        {
            _text = value.RemoveNullCharacters();
            _rawText ??= _text;
        }
    }

    public string? Overview { get; set; }

    public string? SectionTitles { get; set; }
    public PageStatus Status { get; set; }

    /// <summary>
    /// Set when the model refused to transcribe the page: the provider's refusal category, or
    /// <c>written</c> for a refusal the model wrote as the page's text. The page then has
    /// <see cref="PageStatus.OCRNeedsReview"/> and no text. Null for every other page.
    /// </summary>
    [StringLength(64)]
    public string? RefusalCategory { get; set; }

    [NotMapped]
    public BinaryContent? Image { get; set; }

    [NotMapped]
    public MediaType ImageMediaType { get; set; } = MediaType.ImagePng;

    [NotMapped]
    public bool IsOcrImageOnly => Status is PageStatus.OCRNeedsReview;

    [NotMapped]
    public string? ReadableText => PageText.ReadableOf(this);

    internal Page()
    {
    }

    public Page(Document document, int pageNumber, int filePageNumber)
    {
        Document = document;
        DocumentId = document.Id;
        document?.Pages.Add(this);
        PageNumber = pageNumber;
        FilePageNumber = filePageNumber;
    }

    public override string ToString()
    {
        return $"{Document?.Name}, page {PageNumber}";
    }
}

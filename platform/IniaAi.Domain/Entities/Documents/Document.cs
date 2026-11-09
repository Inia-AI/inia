using IniaAi.Common;
using IniaAi.Common.Enums;
using IniaAi.Domain.Common;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows.Templates;
using IniaAi.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Documents;

/// <summary>
/// Represents a document entity.
/// </summary>
public class Document : Entity<Document>
{
    public const int MAX_NAME_LENGTH = 250;

    /// <summary>The first of <see cref="Sources"/> on the copy a confidential run makes of a document it anonymises.</summary>
    public const string ANONYMISER_SOURCE = "Anonymiser";
    public const int MAX_PROCESSING_ERROR_LENGTH = 4000;

    /// <summary>
    /// The most text the platform accepts by value in one request — a pasted document, a case
    /// baseline, a run candidate, a dry-run sample.
    /// </summary>
    public const int MAX_TEXT_LENGTH = 2_000_000;

    public string OrganizationId { get; set; } = string.Empty;
    public string FileMetadataId { get; private set; } = string.Empty;

    public Organization? Organization { get; set; }

    [ForeignKey("FileMetadataId")]
    [DeleteBehavior(DeleteBehavior.Cascade)]
    public FileMetadata? FileMetadata { get; set; }

    public ICollection<Page> Pages { get; private set; } = [];
    public ICollection<Counterparty> Counterparties { get; private set; } = [];

    public DocumentCategory? DocumentCategory { get; set; }

    [Required]
    [StringLength(MAX_NAME_LENGTH)]
    public string Name { get; set; } = string.Empty;

    public MediaType MediaType { get; set; } = MediaType.Unknown;

    [StringLength(1000)]
    public string? Description { get; set; }

    public List<string> Sources { get; set; } = [];

    public string? Topics { get; set; }

    public string? TableOfContents { get; set; }

    public string? UserId { get; set; }

    public string? WorkflowTypeId { get; set; } = default!;

    public string? DocumentCategoryId { get; set; }

    public string? Class { get; set; } = default!;

    [StringLength(64)]
    public string? OverviewPromptHash { get; set; }

    /// <summary>
    /// True when the uploaded file was password-protected and stored as a decrypted working copy.
    /// Never reset once set, so provenance survives re-uploads that dedup onto this document.
    /// </summary>
    public bool WasPasswordProtected { get; set; }

    /// <summary>
    /// True when the intake gate found macros in the uploaded file: a workbook that carries a VBA
    /// project. The platform admits such files and keeps this mark so they can be found later.
    /// Never reset once set, like <see cref="WasPasswordProtected"/>.
    /// </summary>
    public bool HasMacros { get; set; }

    /// <summary>
    /// The pages the platform counts for the file, read from its bytes at upload with no model call, by the rule of the
    /// page counter (<c>DocumentPageCounter</c>), or as the email intake makes it. For an email, the pages of its body.
    /// Null when the file could not be read or makes no pages, and on documents stored before the count existed until
    /// they are uploaded again.
    /// </summary>
    public int? CountedPages { get; set; }

    /// <summary>
    /// For an email, the pages of the files a run can extract from it, counted at upload by the same rule.
    /// <see cref="CountedPages"/> then holds the pages of its body. Null for any other file, and when the email could
    /// not be read.
    /// </summary>
    public int? CountedAttachmentPages { get; set; }

    /// <summary>
    /// For an email, the identifier that the documents a run makes from it carry in <see cref="Sources"/>, read at
    /// upload. A run that holds those documents reads them, not the email again.
    /// </summary>
    public string? EmailIdentifier { get; set; }

    /// <summary>
    /// When standalone processing was last requested for this document; null when pages
    /// only ever came from a workflow execution. Set by <see cref="RequestProcessing"/>, and
    /// the concurrency token of the standalone path: two requests racing to stamp it, or a
    /// stamp racing a write from an older read, resolve to one winner instead of two attempts.
    /// </summary>
    [ConcurrencyCheck]
    public DateTime? ProcessingRequestedAt { get; private set; }

    /// <summary>
    /// When the processing pipeline last completed a pass over this document, on any path
    /// (<see cref="RecordProcessed"/>) — a pass that found the pages already there still
    /// refreshes overviews and embeddings. Null on rows processed before this stamp existed,
    /// whose pages still stand.
    /// </summary>
    public DateTime? ProcessedAt { get; private set; }

    /// <summary>
    /// Why the last standalone processing attempt failed; cleared by the next request.
    /// </summary>
    [StringLength(MAX_PROCESSING_ERROR_LENGTH)]
    public string? ProcessingError { get; private set; }

    [NotMapped]
    [JsonIgnore]
    public DocumentProcessingState ProcessingState => DocumentProcessingStates.Evaluate(this);

    public void RequestProcessing(DateTime requestedAt)
    {
        ProcessingRequestedAt = requestedAt;
        ProcessingError = null;
    }

    public void RecordProcessed(DateTime processedAt)
    {
        ProcessedAt = processedAt;
        ProcessingError = null;
    }

    public void RecordProcessingFailure(string message)
    {
        ProcessingError = message.Truncate(MAX_PROCESSING_ERROR_LENGTH);
    }

    /// <summary>
    /// The display name with the real file extension reattached — <see cref="Name"/> is
    /// extensionless by design (the extension lives in <see cref="Documents.FileMetadata.Extension"/>),
    /// but a rename may bake the extension back into it, hence the suffix guard.
    /// </summary>
    [NotMapped]
    [JsonIgnore]
    public string FileName => FileNameOf(Name, FileMetadata?.Extension);

    /// <summary>
    /// <see cref="FileName"/> from its two columns, for a query that projects them rather than
    /// loading the document with its file metadata.
    /// </summary>
    public static string FileNameOf(string name, string? extension)
    {
        return name.TrimEnd().WithExtension(extension ?? string.Empty);
    }

    /// <summary>
    /// Represents the current document information, during processing or analysis.
    /// This property is not mapped to the database and is used for transient
    /// state management.
    /// </summary>
    [NotMapped]
    [JsonIgnore]
    public DocumentInfo? CurrentDocumentInfo { get; set; }

    internal Document()
    {
    }

    public Document(FileMetadata fileMetadata, MediaType mediaType)
    {
        OrganizationId = fileMetadata.OrganizationId;
        FileMetadataId = fileMetadata.Id;
        Name = fileMetadata.Name;
        MediaType = mediaType;
        fileMetadata.DocumentId = Id;
        FileMetadata = fileMetadata;
    }

    public Page? this[int pageNumber] => Pages.FirstOrDefault(p => p.PageNumber == pageNumber);
}

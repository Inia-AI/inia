using IniaAi.Domain.Entities.Workflows.Blocks;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq.Expressions;

namespace IniaAi.Domain.Entities.Documents;

/// <summary>
/// This class is used to store information about a document that is being
/// processed by <c>DocumentProcessing</c>
/// In the document's collection, the key is the document's information and the
/// value is the document's content
/// </summary>
public partial class DocumentInfo
{
    public const int MAX_EXCLUSION_REASON_LENGTH = 64;

    [Key]
    public string Id { get; private set; } = Guid.NewGuid().ToString();
    public string DocumentId { get; set; } = string.Empty;

    public Document? Document { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string? JobId { get; set; }

    /// <summary>
    /// Whether OCR should be used for processing this document.
    /// This is applicable for PDF, JPG, JPEG and PNG documents.
    /// </summary>
    public bool ShouldUseOcr { get; set; }

    /// <summary>
    /// Whether this document should be displayed in the final output.
    /// Some runtime documents are only intermediate and should not be shown to
    /// the user.
    /// </summary>
    [DefaultValue(true)]
    public bool ShouldBeDisplayedInOutput { get; set; } = true;

    /// <summary>
    /// The document class to use when processing a <see cref="Block"/>.
    /// <para>
    /// Document processing only happens for any given query if
    /// <c>CanBeProcessedForBlock(Query)</c> returns true.
    /// The <c>Query.SupportedDocumentClasses</c> property
    /// controls the document classes that are supported for processing by a query.
    /// Document processing only happens for any given block if
    /// <see cref="CanBeProcessedForBlock(Block)"/> returns true.
    /// The <see cref="Block.SupportedDocumentClasses"/> property
    /// controls the document classes that are supported for processing by a block.
    /// </para>
    /// </summary>
    public string DocumentClass { get; set; } = string.Empty;

    /// <summary>
    /// When a stage that runs before document processing took this document out of the run. The
    /// row and its <see cref="JobId"/> stay for audit, but no reader keyed on the job treats the
    /// document as run input from then on. Null while the document is run input.
    /// </summary>
    public DateTime? ExcludedAt { get; private set; }

    /// <summary>
    /// The stable code the excluding stage gave for taking the document out of the run; null
    /// while the document is run input.
    /// </summary>
    [StringLength(MAX_EXCLUSION_REASON_LENGTH)]
    public string? ExclusionReason { get; private set; }

    [NotMapped]
    public bool IsExcludedFromRun => ExcludedAt is not null;

    internal DocumentInfo()
    {
    }

    public DocumentInfo(string documentId)
    {
        DocumentId = documentId;
    }

    /// <summary>
    /// The one definition of a job's run input: the job's rows that no stage took out of the run.
    /// Every reader that treats a job's documents as run input filters with it.
    /// </summary>
    public static Expression<Func<DocumentInfo, bool>> IsRunInputOf(string jobId)
    {
        return documentInfo => documentInfo.JobId == jobId && documentInfo.ExcludedAt == null;
    }

    /// <summary><see cref="IsRunInputOf"/> for a set of jobs: the rows of any of them that no stage took out of its run.</summary>
    public static Expression<Func<DocumentInfo, bool>> IsRunInputOfAny(IQueryable<string?> jobIds)
    {
        return documentInfo => documentInfo.JobId != null && jobIds.Contains(documentInfo.JobId) && documentInfo.ExcludedAt == null;
    }

    public void ExcludeFromRun(string reason, DateTime excludedAt)
    {
        ExcludedAt = excludedAt;
        ExclusionReason = reason;
    }

    /// <summary>
    /// Check whether this <see cref="DocumentInfo"/> can be processed for given block.
    /// </summary>
    public bool CanBeProcessedForBlock(Block block)
    {
        if (block.SupportedDocumentClasses.Contains(Workflows.DocumentClass.KEY_NONE))
        {
            return false;
        }

        return !block.IsScopedToDocumentClasses()
            || block.SupportedDocumentClasses.Contains(DocumentClass);
    }
}

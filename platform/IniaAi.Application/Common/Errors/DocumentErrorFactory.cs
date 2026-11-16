using ErrorOr;

namespace IniaAi.Application.Errors;

/// <summary>
/// The errors the document surface returns, named once. Not-found keeps the default error code
/// every document handler has always answered with, for a missing document and for another
/// organization's alike.
/// </summary>
public static class DocumentErrorFactory
{
    public const string NOT_FOUND_MESSAGE = "Document not found.";
    public const string READ_BY_RUNNING_REPORT_MESSAGE = "A report that is still running reads this document. Delete the document after the report finishes.";

    public static Error NotFound()
    {
        return Error.NotFound(description: NOT_FOUND_MESSAGE);
    }

    /// <summary>For the organization of a deleted document only: anyone else gets the plain not-found.</summary>
    public static Error Deleted()
    {
        return Error.NotFound("Document.Deleted", "The document was deleted, so its pages can no longer be shown.");
    }

    public static Error ReadByRunningReport()
    {
        return Error.Conflict("Document.ReadByRunningReport", READ_BY_RUNNING_REPORT_MESSAGE);
    }

    public static Error PageNotFound(int pageNumber)
    {
        return Error.NotFound("Document.PageNotFound", $"The document has no page {pageNumber}.");
    }

    public static Error NotProcessable(string mediaType)
    {
        return Error.Validation("Document.NotProcessable", $"Documents of type {mediaType} are stored but never processed into pages: emails and legacy binary Word files are not paged; only PDF, images, spreadsheets, CSV, .docx, plain text and markdown are.");
    }

    public static Error ProcessingAlreadyQueued()
    {
        return Error.Conflict("Document.ProcessingAlreadyQueued", "Processing was already requested for this document and has not finished yet.");
    }
}

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

    public static Error NotFound()
    {
        return Error.NotFound(description: NOT_FOUND_MESSAGE);
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

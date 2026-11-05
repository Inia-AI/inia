namespace IniaAi.Domain.Enums;

/// <summary>
/// Represents the processing status of a document page.
/// </summary>
public enum PageStatus
{
    /// <summary>
    /// Status has not been defined.
    /// </summary>
    Undefined = 0,

    /// <summary>
    /// Page has not been processed yet.
    /// </summary>
    NotProcessed,

    /// <summary>
    /// Page text was successfully extracted from the source file.
    /// </summary>
    ProcessedSuccessfully,

    /// <summary>
    /// Page text extraction failed or returned insufficient/gibberish content.
    /// </summary>
    ExtractedWithErrors,

    /// <summary>
    /// Page was marked for OCR processing as a fallback.
    /// </summary>
    OCRFallback,

    /// <summary>
    /// OCR processing completed successfully for this page.
    /// </summary>
    OCRSuccessful,

    /// <summary>
    /// OCR processing failed or returned unusable content.
    /// </summary>
    OCRFailed,

    /// <summary>
    /// OCR produced text, but a fidelity check judged it unreliable. The page is
    /// routed by image instead, and its extracted text is left null.
    /// </summary>
    OCRNeedsReview
}

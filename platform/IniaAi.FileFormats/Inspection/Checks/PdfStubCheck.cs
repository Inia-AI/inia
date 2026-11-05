using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.Documents;
using IniaAi.FileFormats.Pdf;

namespace IniaAi.FileFormats.Inspection.Checks;

/// <summary>
/// Refuses a PDF that is a header with nothing behind it. A damaged PDF that still carries
/// objects passes, because the reader downstream repairs it.
/// </summary>
public sealed class PdfStubCheck : IFileContentCheck
{
    public FileInspection? Run(FileInspectionContext context)
    {
        DetectedContent detected = context.RequireDetected();
        return detected.PlatformMediaType is MediaType.ApplicationPdf && PdfUtils.IsHeaderOnlyStub(context.Bytes)
            ? FileInspection.Refuse(
                IntakeRefusalReasons.MALFORMED_FILE,
                "The PDF has no content and cannot be opened. Save or download it again, then upload the new copy.",
                detected)
            : null;
    }
}

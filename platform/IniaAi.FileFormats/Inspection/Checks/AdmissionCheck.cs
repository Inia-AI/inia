using IniaAi.Common.Enums;
using IniaAi.Common.Extensions;
using IniaAi.Domain.Entities.Documents;

namespace IniaAi.FileFormats.Inspection.Checks;

/// <summary>
/// The allowlist keys off the detected type and nothing else. The set is the one the document
/// pipeline reads: every type that produces pages, plus the two email types it converts first.
/// </summary>
public sealed class AdmissionCheck : IFileContentCheck
{
    public FileInspection? Run(FileInspectionContext context)
    {
        return Refusal(context.RequireDetected());
    }

    /// <summary>The refusal a detected type earns, or null when the platform reads it.</summary>
    public static FileInspection? Refusal(DetectedContent detected)
    {
        if (detected == DetectedContent.Unrecognised)
        {
            return FileInspection.Refuse(
                IntakeRefusalReasons.FILE_TYPE_MISMATCH,
                "The platform cannot tell what type of file this is. Upload a PDF, Word, Excel, CSV, PNG or JPEG file instead.",
                detected);
        }

        if (detected.PlatformMediaType is not MediaType mediaType || !(mediaType.ProducesPages() || mediaType.IsEmail()))
        {
            return FileInspection.Refuse(
                IntakeRefusalReasons.FILE_TYPE_MISMATCH,
                $"The platform does not accept this type of file ({detected.IanaMediaType}). Upload a PDF, Word, Excel, CSV, PNG or JPEG file instead.",
                detected);
        }

        return null;
    }
}

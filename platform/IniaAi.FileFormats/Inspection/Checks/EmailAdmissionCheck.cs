using IniaAi.Common.Enums;
using IniaAi.Common.Extensions;
using IniaAi.Domain.Entities.Documents;

namespace IniaAi.FileFormats.Inspection.Checks;

/// <summary>
/// The admission rule of the watched-directory drop: an email message, decided by the detected
/// type and nothing else. Runs after <see cref="AdmissionCheck"/>, so a type the platform never
/// reads keeps that check's wording and a readable document that is not an email gets this one.
/// </summary>
public sealed class EmailAdmissionCheck : IFileContentCheck
{
    public const string NOT_A_MESSAGE_FILE = "Only email files (.eml or .msg) are accepted here. Use the email itself, not one of its attachments.";

    public FileInspection? Run(FileInspectionContext context)
    {
        DetectedContent detected = context.RequireDetected();
        return detected.PlatformMediaType is MediaType mediaType && mediaType.IsEmail()
            ? null
            : FileInspection.Refuse(IntakeRefusalReasons.FILE_TYPE_MISMATCH, NOT_A_MESSAGE_FILE, detected);
    }
}

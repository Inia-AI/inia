namespace IniaAi.FileFormats.Inspection.Checks;

/// <summary>
/// An encrypted file is routed, not refused. <see cref="EncryptedContainerProbe"/> names the
/// container from the bytes; the verdict carries it so the caller hands the bytes to unlock by
/// that name and never by the filename.
/// </summary>
public sealed class EncryptionCheck : IFileContentCheck
{
    public FileInspection? Run(FileInspectionContext context)
    {
        DetectedContent detected = context.RequireDetected();
        return EncryptedContainerProbe.Detect(context.Bytes) is EncryptedContainer container
            ? FileInspection.NeedsUnlock(container, DetailFor(container), detected)
            : null;
    }

    private static string DetailFor(EncryptedContainer container)
    {
        return container switch
        {
            EncryptedContainer.Pdf => "The PDF is encrypted; a password is needed before it can be inspected.",
            EncryptedContainer.LegacyWorkbook => "The workbook is encrypted; a password is needed before it can be inspected.",
            EncryptedContainer.OfficePackage => "The file is an encrypted Office container; its type is known after unlock.",
            _ => throw new ArgumentOutOfRangeException(nameof(container), container, "No unlock detail is written for this container."),
        };
    }
}

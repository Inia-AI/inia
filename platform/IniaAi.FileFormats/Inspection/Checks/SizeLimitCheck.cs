using IniaAi.Domain.Entities.Documents;

namespace IniaAi.FileFormats.Inspection.Checks;

public sealed class SizeLimitCheck : IFileContentCheck
{
    public FileInspection? Run(FileInspectionContext context)
    {
        if (context.Bytes.Length == 0)
        {
            return FileInspection.Refuse(IntakeRefusalReasons.EMPTY_FILE, "The file is empty. Check that it is the right file, then upload it again.", null);
        }

        if (context.Bytes.Length > context.Limits.MaxFileBytes)
        {
            return FileInspection.Refuse(
                IntakeRefusalReasons.FILE_TOO_LARGE,
                TooLargeDetail(context.Bytes.Length, context.Limits.MaxFileBytes),
                null);
        }

        return null;
    }

    public static string TooLargeDetail(long sizeBytes, long limitBytes)
    {
        return $"The file is {FileSizeText.Of(sizeBytes)}, more than the limit of {FileSizeText.Of(limitBytes)}. Upload a smaller copy, or split the document into smaller files.";
    }
}

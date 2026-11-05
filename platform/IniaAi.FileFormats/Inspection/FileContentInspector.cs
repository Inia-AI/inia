using IniaAi.FileFormats.Inspection.Checks;

namespace IniaAi.FileFormats.Inspection;

/// <summary>
/// Decides from the bytes whether a file may enter the platform. The filename never admits a
/// file: it refuses one whose name claims a signed format the bytes are not, and it refines which
/// text type a signature-less text file is. The sequence runs cheapest first and refuses a
/// structural bomb before any check expands the content.
/// </summary>
public sealed class FileContentInspector
{
    public static readonly IReadOnlyList<IFileContentCheck> DefaultChecks =
    [
        new SizeLimitCheck(),
        new ArchiveStructureCheck(),
        new ContentTypeDetectionCheck(),
        new EncryptionCheck(),
        new AdmissionCheck(),
        new ClaimedFormatCheck(),
        new PdfStubCheck(),
        new ImageDimensionCheck(),
    ];

    public static FileContentInspector Default { get; } = new(DefaultChecks);

    public static FileContentInspector EmailMessages { get; } = new(
    [
        new SizeLimitCheck(),
        new ArchiveStructureCheck(),
        new ContentTypeDetectionCheck(),
        new AdmissionCheck(),
        new EmailAdmissionCheck(),
    ]);

    private readonly IReadOnlyList<IFileContentCheck> _checks;

    public FileContentInspector(IReadOnlyList<IFileContentCheck> checks)
    {
        _checks = checks;
    }

    public FileInspection Inspect(byte[] bytes, string fileName, FileInspectionLimits limits)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(limits);

        var context = new FileInspectionContext(bytes, fileName, limits);
        foreach (IFileContentCheck check in _checks)
        {
            FileInspection? verdict = check.Run(context);
            if (verdict is not null)
            {
                return verdict;
            }
        }

        return FileInspection.Accept(context.Detected);
    }
}

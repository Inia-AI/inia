using FileSignatures;
using Formats = FileSignatures.Formats;
using IniaAi.Domain.Entities.Documents;
using OpenMcdf;
using System.Xml;

namespace IniaAi.FileFormats.Inspection.Checks;

/// <summary>
/// Records what the bytes are. Signature formats come from FileSignatures, which reads the zip
/// directory to tell an Office package from a plain zip and the compound-file stream names to
/// tell a legacy workbook from an Outlook message; its media-type strings are the platform's
/// own enum-member values, so the platform type is a parse of that string. A signature-less
/// file is text or unrecognised. This check never refuses on the type; <see cref="AdmissionCheck"/> does.
/// </summary>
public sealed class ContentTypeDetectionCheck : IFileContentCheck
{
    private const string VBA_PROJECT_ENTRY = "vbaProject.bin";

    private static readonly FileFormatInspector _signatures = new();

    public FileInspection? Run(FileInspectionContext context)
    {
        FileFormat? format;
        try
        {
            using MemoryStream stream = context.OpenStream();
            format = _signatures.DetermineFileFormat(stream);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or XmlException or OpenMcdf.FileFormatException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return FileInspection.Refuse(
                IntakeRefusalReasons.MALFORMED_FILE,
                FileInspection.DAMAGED_FILE,
                null);
        }

        context.Detected = format is null
            ? TextContentSniffer.Sniff(context.Bytes, context.FileName) ?? DetectedContent.Unrecognised
            : DetectedContent.FromIanaMediaType(format.MediaType, HasMacros(format, context));
        return null;
    }

    private static bool HasMacros(FileFormat format, FileInspectionContext context)
    {
        return format switch
        {
            Formats.Zip => context.ArchiveEntries?.Any(static entry => entry.EndsWith(VBA_PROJECT_ENTRY, StringComparison.OrdinalIgnoreCase)) ?? false,
            Formats.CompoundFileBinary => CompoundFileProbe.HasVbaProject(context.Bytes),
            _ => false,
        };
    }
}

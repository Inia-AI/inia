using IniaAi.Domain.Entities.Documents;
using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;

namespace IniaAi.FileFormats.Inspection.Checks;

/// <summary>
/// Reads only the zip directory. The entry count comes from the end-of-central-directory record
/// at the tail, before the directory itself is read, because reading the directory of an
/// archive made of nothing but entries costs memory proportional to that count. The declared
/// sizes then come from the directory without opening an entry, and the runtime never lets an
/// entry produce more than its declared length.
/// </summary>
public sealed class ArchiveStructureCheck : IFileContentCheck
{
    private const string GROUPED = "N0";
    private const int END_OF_CENTRAL_DIRECTORY_LENGTH = 22;
    private const int MAX_ARCHIVE_COMMENT_LENGTH = ushort.MaxValue;
    private const int TOTAL_ENTRIES_OFFSET = 10;
    private const string PACKAGE_MANIFEST_ENTRY = "[Content_Types].xml";

    private static readonly byte[] _zipSignature = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] _endOfCentralDirectorySignature = [0x50, 0x4B, 0x05, 0x06];

    public FileInspection? Run(FileInspectionContext context)
    {
        if (!context.StartsWith(_zipSignature))
        {
            return null;
        }

        int? declaredEntries = ReadDeclaredEntryCount(context.Bytes);
        if (declaredEntries is null)
        {
            return Malformed();
        }

        if (declaredEntries > context.Limits.MaxArchiveEntries)
        {
            return TooManyEntries(declaredEntries.Value, context.Limits);
        }

        try
        {
            using MemoryStream stream = context.OpenStream();
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            return CheckDirectory(archive, context);
        }
        catch (InvalidDataException)
        {
            return Malformed();
        }
    }

    private static int? ReadDeclaredEntryCount(byte[] bytes)
    {
        ReadOnlySpan<byte> tail = bytes.AsSpan(Math.Max(0, bytes.Length - END_OF_CENTRAL_DIRECTORY_LENGTH - MAX_ARCHIVE_COMMENT_LENGTH));
        int record = tail.LastIndexOf(_endOfCentralDirectorySignature);
        if (record < 0 || tail.Length - record < END_OF_CENTRAL_DIRECTORY_LENGTH)
        {
            return null;
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(tail[(record + TOTAL_ENTRIES_OFFSET)..]);
    }

    private static FileInspection? CheckDirectory(ZipArchive archive, FileInspectionContext context)
    {
        FileInspectionLimits limits = context.Limits;
        if (archive.Entries.Count > limits.MaxArchiveEntries)
        {
            return TooManyEntries(archive.Entries.Count, limits);
        }

        long expanded = 0;
        var names = new List<string>(archive.Entries.Count);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            expanded += entry.Length;
            names.Add(entry.FullName);
            if (entry.FullName == PACKAGE_MANIFEST_ENTRY && entry.Length > limits.MaxPackageManifestBytes)
            {
                return FileInspection.Refuse(
                    IntakeRefusalReasons.STRUCTURAL_LIMIT,
                    "The file's internal index is too large for a document, so it cannot be opened. Save it again from the program that made it, then upload the new copy.",
                    null);
            }
        }

        if (expanded > limits.MaxExpandedBytes)
        {
            return FileInspection.Refuse(
                IntakeRefusalReasons.STRUCTURAL_LIMIT,
                $"The file unpacks to {FileSizeText.Of(expanded)}, more than the limit of {FileSizeText.Of(limits.MaxExpandedBytes)}. Split the content into smaller files, then upload them.",
                null);
        }

        if (expanded > limits.CompressionRatioFloorBytes && expanded / context.Bytes.Length > limits.MaxCompressionRatio)
        {
            return FileInspection.Refuse(
                IntakeRefusalReasons.STRUCTURAL_LIMIT,
                "The file unpacks to far more data than its size suggests, so it cannot be opened. Save it again from the program that made it, then upload the new copy.",
                null);
        }

        context.ArchiveEntries = names;
        return null;
    }

    private static FileInspection TooManyEntries(int entries, FileInspectionLimits limits)
    {
        return FileInspection.Refuse(
            IntakeRefusalReasons.STRUCTURAL_LIMIT,
            $"The file holds {entries.ToString(GROUPED, CultureInfo.InvariantCulture)} items, more than the limit of {limits.MaxArchiveEntries.ToString(GROUPED, CultureInfo.InvariantCulture)}. Split the content into smaller files, then upload them.",
            null);
    }

    private static FileInspection Malformed()
    {
        return FileInspection.Refuse(
            IntakeRefusalReasons.MALFORMED_FILE,
            FileInspection.DAMAGED_FILE,
            null);
    }
}

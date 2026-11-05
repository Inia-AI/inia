using OpenMcdf;

namespace IniaAi.FileFormats.Inspection.Checks;

/// <summary>
/// The compound-file questions the checks ask, each answered from the root directory through
/// OpenMcdf. Aspose's own format detector is not used here on purpose: it walks the sector
/// counts a header declares rather than the bytes present, so six mutated header bytes cost it
/// tens of gigabytes, while OpenMcdf sizes its reads from the stream and reads the same bytes
/// in milliseconds. A container OpenMcdf cannot open answers "no" to every question.
/// </summary>
internal static class CompoundFileProbe
{
    internal const string ENCRYPTED_PACKAGE_STREAM = "EncryptedPackage";
    internal const string ENCRYPTION_INFO_STREAM = "EncryptionInfo";
    private const string VBA_PROJECT_STORAGE = "_VBA_PROJECT_CUR";
    private const string LEGACY_MACROS_STORAGE = "Macros";
    private const string WORKBOOK_STREAM = "Workbook";
    private const string LEGACY_BOOK_STREAM = "Book";
    private const ushort BIFF_FILEPASS = 0x002F;
    private const ushort BIFF_EOF = 0x000A;
    private const int BIFF_RECORDS_TO_SCAN = 64;

    private static readonly byte[] _signature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    internal static ReadOnlySpan<byte> Signature => _signature;

    internal static bool IsCompoundFile(byte[] bytes)
    {
        return bytes.AsSpan().StartsWith(_signature);
    }

    internal static bool IsEncryptedPackage(byte[] bytes)
    {
        return Ask(bytes, static root =>
            root.TryOpenStream(ENCRYPTED_PACKAGE_STREAM, out _) || root.TryOpenStream(ENCRYPTION_INFO_STREAM, out _));
    }

    internal static bool IsRightsManagedPackage(byte[] bytes)
    {
        return Ask(bytes, static root =>
            root.TryOpenStream(ENCRYPTED_PACKAGE_STREAM, out _) && !root.TryOpenStream(ENCRYPTION_INFO_STREAM, out _));
    }

    internal static bool HasVbaProject(byte[] bytes)
    {
        return Ask(bytes, static root =>
            root.TryOpenStorage(VBA_PROJECT_STORAGE, out _) || root.TryOpenStorage(LEGACY_MACROS_STORAGE, out _));
    }

    internal static bool WorkbookHasPasswordRecord(byte[] bytes)
    {
        return Ask(bytes, static root =>
        {
            if (!root.TryOpenStream(WORKBOOK_STREAM, out CfbStream? workbook) && !root.TryOpenStream(LEGACY_BOOK_STREAM, out workbook))
            {
                return false;
            }

            using (workbook)
            {
                return LeadingBiffRecordsContainFilePass(workbook);
            }
        });
    }

    private static bool LeadingBiffRecordsContainFilePass(Stream workbook)
    {
        Span<byte> header = stackalloc byte[4];
        for (int i = 0; i < BIFF_RECORDS_TO_SCAN; i++)
        {
            if (workbook.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
            {
                return false;
            }

            ushort id = (ushort)(header[0] | (header[1] << 8));
            ushort length = (ushort)(header[2] | (header[3] << 8));
            if (id == BIFF_FILEPASS)
            {
                return true;
            }

            if (id == BIFF_EOF)
            {
                return false;
            }

            _ = workbook.Seek(length, SeekOrigin.Current);
        }

        return false;
    }

    private static bool Ask(byte[] bytes, Func<RootStorage, bool> question)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using RootStorage root = RootStorage.Open(stream, StorageModeFlags.LeaveOpen);
            return question(root);
        }
        catch (Exception ex) when (ex is IOException or OpenMcdf.FileFormatException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }
}

using System.IO.Compression;

namespace IniaAi.FileFormats.Inspection;

/// <summary>
/// Which entries of a zip archive carry content the platform reads: a file, not a directory
/// entry, not a symbolic link and not the resource-fork metadata a macOS archive carries under
/// <c>__MACOSX/</c>.
/// </summary>
public static class ZipEntries
{
    private const string MACOS_METADATA_PREFIX = "__MACOSX/";

    // An archive made on Unix keeps each entry's file mode in the high 16 bits of its external attributes.
    private const int UNIX_MODE_SHIFT = 16;
    private const int UNIX_FILE_TYPE_MASK = 0xF000;
    private const int UNIX_SYMBOLIC_LINK = 0xA000;

    public static bool IsContent(ZipArchiveEntry entry)
    {
        return entry.Name.Length > 0
            && !entry.FullName.StartsWith(MACOS_METADATA_PREFIX, StringComparison.OrdinalIgnoreCase)
            && !IsSymbolicLink(entry);
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry)
    {
        return ((entry.ExternalAttributes >> UNIX_MODE_SHIFT) & UNIX_FILE_TYPE_MASK) == UNIX_SYMBOLIC_LINK;
    }
}

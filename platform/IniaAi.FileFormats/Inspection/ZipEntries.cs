using System.IO.Compression;

namespace IniaAi.FileFormats.Inspection;

/// <summary>
/// Which entries of a zip archive carry content the platform reads: a file, not a directory
/// entry and not the resource-fork metadata a macOS archive carries under <c>__MACOSX/</c>.
/// </summary>
public static class ZipEntries
{
    private const string MACOS_METADATA_PREFIX = "__MACOSX/";

    public static bool IsContent(ZipArchiveEntry entry)
    {
        return entry.Name.Length > 0
            && !entry.FullName.StartsWith(MACOS_METADATA_PREFIX, StringComparison.OrdinalIgnoreCase);
    }
}

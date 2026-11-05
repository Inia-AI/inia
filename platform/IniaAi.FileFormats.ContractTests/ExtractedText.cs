using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace IniaAi.FileFormats.ContractTests;

internal static partial class ExtractedText
{
    private const string XML_PART_EXTENSION = ".xml";

    /// <summary>The text with each run of white space read as one space: readers lay out a line's spacing and breaks differently.</summary>
    public static string Words(string text)
    {
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// The text of every XML part of an Office package, tags removed. Part names, namespace prefixes
    /// and how a writer splits a paragraph into runs differ between libraries.
    /// </summary>
    public static string PackageText(byte[] package)
    {
        using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        var text = new StringBuilder();

        foreach (ZipArchiveEntry entry in archive.Entries.Where(entry => entry.FullName.EndsWith(XML_PART_EXTENSION, StringComparison.OrdinalIgnoreCase)))
        {
            using var reader = new StreamReader(entry.Open());
            _ = text.Append(XmlTag().Replace(reader.ReadToEnd(), string.Empty));
        }

        return text.ToString();
    }

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex XmlTag();
}

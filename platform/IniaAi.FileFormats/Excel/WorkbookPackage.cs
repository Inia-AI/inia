using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace IniaAi.FileFormats.Excel;

/// <summary>
/// Rewrites a workbook package at the level of its zip entries, so a part that is missing or that a library fails to
/// load never has to load.
/// </summary>
internal static class WorkbookPackage
{
    private const string RELATIONSHIPS_EXTENSION = ".rels";
    private const string CONTENT_TYPES_PART = "[Content_Types].xml";
    private const string PART_NAME_ATTRIBUTE = "PartName";
    private const string TYPE_ATTRIBUTE = "Type";
    private const string TARGET_ATTRIBUTE = "Target";
    private const string NAME_ATTRIBUTE = "name";
    private const string SHEETS_ELEMENT = "sheets";
    private const string SHEET_ELEMENT = "sheet";
    private const string PACKAGE_RELATIONSHIPS_PART = "_rels/.rels";
    private const string OFFICE_DOCUMENT_RELATIONSHIP_TYPE = "/officeDocument";

    private static readonly XNamespace _spreadsheetMl = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace _relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace _contentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";

    private static readonly string[] _droppedFolders = ["xl/persons/", "xl/threadedComments/", "xl/drawings/", "xl/media/"];
    private static readonly string[] _droppedPartPrefixes = ["xl/comments"];
    private static readonly string[] _droppedRelationshipTypes = ["/person", "/threadedComment", "/comments", "/vmlDrawing", "/drawing", "/image"];
    private static readonly string[] _sheetFolders = ["xl/worksheets/", "xl/chartsheets/"];
    private static readonly XName[] _drawingReferences = [_spreadsheetMl + "drawing", _spreadsheetMl + "legacyDrawing", _spreadsheetMl + "legacyDrawingHF"];

    /// <summary>A copy of the workbook without its comments, threaded comments and their people, drawings and pictures.</summary>
    public static MemoryStream WithoutCommentsAndDrawings(Stream source)
    {
        var copy = new MemoryStream();
        using (var original = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true))
        using (var stripped = new ZipArchive(copy, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (ZipArchiveEntry entry in original.Entries)
            {
                if (IsDropped(entry.FullName))
                {
                    continue;
                }

                using Stream input = entry.Open();
                using Stream output = stripped.CreateEntry(entry.FullName, CompressionLevel.Fastest).Open();
                if (IsFolder(entry))
                {
                    continue;
                }

                if (entry.FullName.EndsWith(RELATIONSHIPS_EXTENSION, StringComparison.OrdinalIgnoreCase))
                {
                    Rewrite(input, output, document => Remove(document.Descendants(_relationships + "Relationship"), IsDroppedRelationship));
                }
                else if (entry.FullName == CONTENT_TYPES_PART)
                {
                    Rewrite(input, output, document => Remove(document.Descendants(_contentTypes + "Override"), IsDroppedOverride));
                }
                else if (IsSheet(entry.FullName))
                {
                    Rewrite(input, output, document => Remove(document.Descendants().Where(element => _drawingReferences.Contains(element.Name)), _ => true));
                }
                else
                {
                    input.CopyTo(output);
                }
            }
        }

        copy.Position = 0;
        return copy;
    }

    /// <summary>The workbook's defined names, read from its zip, so a part that the package names but lacks cannot stop the read.</summary>
    public static List<XElement> DefinedNames(Stream source)
    {
        using var package = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        XDocument? workbook = WorkbookPart(package) is { } part ? Part(package, part) : null;
        return workbook?.Descendants(_spreadsheetMl + "definedName").ToList() ?? [];
    }

    /// <inheritdoc cref="WorkbookSheetNames.Read"/>
    public static HashSet<string> SheetNames(Stream source)
    {
        using var package = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        if (WorkbookPart(package) is not { } part || package.GetEntry(part) is not { } entry)
        {
            return names;
        }

        using Stream stream = entry.Open();
        using XmlReader reader = PackageXml.Reader(stream);
        while (reader.Read())
        {
            if (reader.LocalName == SHEETS_ELEMENT && (reader.NodeType == XmlNodeType.EndElement || reader.IsEmptyElement))
            {
                break;
            }

            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == SHEET_ELEMENT && reader.GetAttribute(NAME_ATTRIBUTE) is { } name)
            {
                _ = names.Add(name);
            }
        }

        return names;
    }

    private static string? WorkbookPart(ZipArchive package)
    {
        return Part(package, PACKAGE_RELATIONSHIPS_PART)?
            .Descendants(_relationships + "Relationship")
            .FirstOrDefault(relationship => ((string?)relationship.Attribute(TYPE_ATTRIBUTE) ?? string.Empty)
                .EndsWith(OFFICE_DOCUMENT_RELATIONSHIP_TYPE, StringComparison.OrdinalIgnoreCase))?
            .Attribute(TARGET_ATTRIBUTE)?.Value.TrimStart('/');
    }

    private static XDocument? Part(ZipArchive package, string entryName)
    {
        ZipArchiveEntry? entry = package.GetEntry(entryName);
        if (entry is null)
        {
            return null;
        }

        using Stream stream = entry.Open();
        return PackageXml.Load(stream);
    }

    private static bool IsFolder(ZipArchiveEntry entry)
    {
        return entry.FullName.EndsWith('/');
    }

    private static bool IsDropped(string entryName)
    {
        return _droppedFolders.Any(folder => entryName.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
            || _droppedPartPrefixes.Any(prefix => entryName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSheet(string entryName)
    {
        return _sheetFolders.Any(folder => entryName.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
            && !entryName.EndsWith(RELATIONSHIPS_EXTENSION, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDroppedRelationship(XElement relationship)
    {
        string type = (string?)relationship.Attribute(TYPE_ATTRIBUTE) ?? string.Empty;
        return _droppedRelationshipTypes.Any(suffix => type.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDroppedOverride(XElement contentType)
    {
        return IsDropped(((string?)contentType.Attribute(PART_NAME_ATTRIBUTE) ?? string.Empty).TrimStart('/'));
    }

    private static void Remove(IEnumerable<XElement> elements, Func<XElement, bool> dropped)
    {
        foreach (XElement element in elements.Where(dropped).ToList())
        {
            element.Remove();
        }
    }

    private static void Rewrite(Stream input, Stream output, Action<XDocument> change)
    {
        XDocument document = PackageXml.Load(input);
        change(document);
        document.Save(output, SaveOptions.DisableFormatting);
    }
}

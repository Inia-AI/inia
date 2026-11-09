using IniaAi.FileFormats.Excel;
using System.IO.Compression;
using System.Xml;

namespace IniaAi.FileFormats.Pages;

/// <summary>
/// Measures the text of a Word or Excel package by streaming its XML parts once, without building a document model, so
/// an upload is measured in milliseconds. It reads what the pipeline reads: a workbook's visible sheets up to
/// <see cref="ExcelUtils.MAX_COLUMNS"/> columns with formulas as their cached values, and a Word body without the
/// fallback copies of alternate content. A size a package declares about itself, such as a sheet's dimension or the
/// word count in its properties, is never trusted. A part longer than <see cref="MAX_PART_CHARACTERS"/>, or more than
/// <see cref="MAX_SHARED_STRINGS"/> shared strings, stops the count, so a crafted package cannot hold the counter's memory.
/// </summary>
internal static class OfficePackageText
{
    private const string WORD_NAMESPACE = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string RELATIONSHIP_NAMESPACE = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string COMPATIBILITY_NAMESPACE = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private const string DOCUMENT_RELATIONSHIP = "/officeDocument";
    private const string IMAGE_RELATIONSHIP = "/image";
    private const string SHARED_STRINGS_RELATIONSHIP = "/sharedStrings";
    private const string VISIBLE_SHEET = "visible";
    private const int LONGEST_GENERAL_NUMBER = 11;
    private const long MAX_PART_CHARACTERS = 200_000_000;
    private const int MAX_SHARED_STRINGS = 10_000_000;

    private static readonly XmlReaderSettings _xml = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        MaxCharactersInDocument = MAX_PART_CHARACTERS,
    };

    /// <summary>The characters of a Word body and the number of distinct pictures it shows.</summary>
    public static (long Characters, int Pictures) Word(byte[] bytes)
    {
        using var package = new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);
        string body = MainPart(package);
        Dictionary<string, Relationship> relationships = Relationships(package, body);

        long characters = 0;
        HashSet<string> pictures = [];
        using XmlReader reader = XmlReader.Create(Entry(package, body).Open(), _xml);
        while (!reader.EOF)
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                _ = reader.Read();
                continue;
            }

            if (reader.NamespaceURI == COMPATIBILITY_NAMESPACE && reader.LocalName == "Fallback")
            {
                reader.Skip();
                continue;
            }

            if (reader.NamespaceURI == WORD_NAMESPACE && reader.LocalName == "t")
            {
                characters += reader.ReadElementContentAsString().Length;
                continue;
            }

            if (reader.LocalName is "blip" or "imagedata"
                && (reader.GetAttribute("embed", RELATIONSHIP_NAMESPACE) ?? reader.GetAttribute("id", RELATIONSHIP_NAMESPACE)) is { } id
                && relationships.TryGetValue(id, out Relationship? picture)
                && picture.Type.EndsWith(IMAGE_RELATIONSHIP, StringComparison.OrdinalIgnoreCase))
            {
                _ = pictures.Add(picture.Target);
            }

            _ = reader.Read();
        }

        return (characters, pictures.Count);
    }

    /// <summary>The characters of a workbook's visible sheets as table text, and the row pages that hold any text.</summary>
    public static (long Characters, int RowPages) Workbook(byte[] bytes)
    {
        using var package = new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);
        string workbook = MainPart(package);
        Dictionary<string, Relationship> relationships = Relationships(package, workbook);
        List<int> sharedStrings = relationships.Values.FirstOrDefault(relationship => relationship.Type.EndsWith(SHARED_STRINGS_RELATIONSHIP, StringComparison.OrdinalIgnoreCase)) is { } strings
            && package.GetEntry(strings.Target) is { } stringsEntry
                ? SharedStringLengths(stringsEntry)
                : [];

        long characters = 0;
        int rowPages = 0;
        foreach (string sheet in VisibleSheets(package, workbook, relationships))
        {
            if (package.GetEntry(sheet) is not { } sheetEntry)
            {
                continue;
            }

            SheetMeasure measure = Sheet(sheetEntry, sharedStrings);
            characters += measure.Characters;
            rowPages += measure.RowPages();
        }

        return (characters, rowPages);
    }

    private static IEnumerable<string> VisibleSheets(ZipArchive package, string workbook, Dictionary<string, Relationship> relationships)
    {
        using XmlReader reader = XmlReader.Create(Entry(package, workbook).Open(), _xml);
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element
                && reader.LocalName == "sheet"
                && (reader.GetAttribute("state") ?? VISIBLE_SHEET) == VISIBLE_SHEET
                && reader.GetAttribute("id", RELATIONSHIP_NAMESPACE) is { } id
                && relationships.TryGetValue(id, out Relationship? sheet))
            {
                yield return sheet.Target;
            }
        }
    }

    private static SheetMeasure Sheet(ZipArchiveEntry entry, List<int> sharedStrings)
    {
        var measure = new SheetMeasure();
        int row = -1;
        int column = -1;
        using XmlReader reader = XmlReader.Create(entry.Open(), _xml);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (reader.LocalName == "row")
            {
                row = int.TryParse(reader.GetAttribute("r"), out int number) ? number - 1 : row + 1;
                column = -1;
                continue;
            }

            if (reader.LocalName != "c")
            {
                continue;
            }

            if (reader.GetAttribute("r") is { } reference)
            {
                (column, int? referencedRow) = CellOf(reference);
                row = referencedRow ?? row;
            }
            else
            {
                column++;
            }

            int length = CellLength(reader, reader.GetAttribute("t"), sharedStrings);
            if (column < ExcelUtils.MAX_COLUMNS)
            {
                measure.Add(row, length);
            }
        }

        return measure;
    }

    private static int CellLength(XmlReader reader, string? type, List<int> sharedStrings)
    {
        if (reader.IsEmptyElement)
        {
            return 0;
        }

        int depth = reader.Depth;
        string? value = null;
        int inlineLength = 0;
        while (reader.Read() && reader.Depth > depth)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "v")
            {
                value = reader.ReadElementContentAsString();
                if (reader.Depth <= depth)
                {
                    break;
                }
            }
            else if (reader.NodeType == XmlNodeType.Text && type == "inlineStr")
            {
                inlineLength += reader.Value.Length;
            }
        }

        if (type == "inlineStr")
        {
            return inlineLength;
        }

        string text = value?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return 0;
        }

        return type switch
        {
            "s" => int.TryParse(text, out int index) && index >= 0 && index < sharedStrings.Count ? sharedStrings[index] : 0,
            "b" => text == "1" ? "TRUE".Length : "FALSE".Length,
            "str" or "e" => text.Length,
            _ => Math.Min(text.Length, LONGEST_GENERAL_NUMBER),
        };
    }

    private static List<int> SharedStringLengths(ZipArchiveEntry entry)
    {
        List<int> lengths = [];
        int length = 0;
        bool phonetic = false;
        using XmlReader reader = XmlReader.Create(entry.Open(), _xml);
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "si")
            {
                length = 0;
                if (reader.IsEmptyElement)
                {
                    lengths.Add(0);
                }
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "rPh" && !reader.IsEmptyElement)
            {
                phonetic = true;
            }
            else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "rPh")
            {
                phonetic = false;
            }
            else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "si")
            {
                lengths.Add(length);
            }

            if (lengths.Count > MAX_SHARED_STRINGS)
            {
                throw new InvalidDataException($"The workbook holds more than {MAX_SHARED_STRINGS} distinct texts, more than the platform counts.");
            }
            else if (reader.NodeType is XmlNodeType.Text or XmlNodeType.SignificantWhitespace && !phonetic)
            {
                length += reader.Value.Length;
            }
        }

        return lengths;
    }

    private sealed record Relationship(string Type, string Target);

    private static string MainPart(ZipArchive package)
    {
        return Relationships(package, string.Empty).Values
            .First(relationship => relationship.Type.EndsWith(DOCUMENT_RELATIONSHIP, StringComparison.OrdinalIgnoreCase))
            .Target;
    }

    private static ZipArchiveEntry Entry(ZipArchive package, string path)
    {
        return package.GetEntry(path) ?? throw new InvalidDataException($"The package has no part {path}.");
    }

    private static Dictionary<string, Relationship> Relationships(ZipArchive package, string part)
    {
        int slash = part.LastIndexOf('/');
        string folder = slash < 0 ? string.Empty : part[..slash];
        string relationshipsPart = Resolve(folder, $"_rels/{part[(slash + 1)..]}.rels");
        Dictionary<string, Relationship> relationships = [];
        if (package.GetEntry(relationshipsPart) is not { } entry)
        {
            return relationships;
        }

        using XmlReader reader = XmlReader.Create(entry.Open(), _xml);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Relationship")
            {
                continue;
            }

            string target = reader.GetAttribute("Target") ?? string.Empty;
            if (reader.GetAttribute("TargetMode") != "External")
            {
                target = target.StartsWith('/') ? target.TrimStart('/') : Resolve(folder, target);
            }

            relationships[reader.GetAttribute("Id") ?? string.Empty] = new Relationship(reader.GetAttribute("Type") ?? string.Empty, target);
        }

        return relationships;
    }

    private static string Resolve(string folder, string target)
    {
        List<string> parts = [];
        foreach (string part in $"{folder}/{target}".Split('/'))
        {
            if (part == "..")
            {
                if (parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }
            }
            else if (part is not "." and not "")
            {
                parts.Add(part);
            }
        }

        return string.Join('/', parts);
    }

    private static (int Column, int? Row) CellOf(string reference)
    {
        int column = 0;
        int index = 0;
        while (index < reference.Length && char.IsAsciiLetter(reference[index]))
        {
            column = (column * 26) + (char.ToUpperInvariant(reference[index]) - 'A' + 1);
            index++;
        }

        return (column - 1, int.TryParse(reference.AsSpan(index), out int row) ? row - 1 : null);
    }
}

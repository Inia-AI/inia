using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace IniaAi.Testing.Ooxml;

/// <summary>
/// Reads a saved workbook or Word document as raw Office Open XML, so a test checks the file itself and no
/// implementation's reading of it.
/// </summary>
public static class OoxmlPackage
{
    public static readonly XNamespace SpreadsheetMl = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    public static readonly XNamespace WordprocessingMl = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public static readonly XNamespace MarkupCompatibility = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    private static readonly XNamespace _contentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace _packageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";

    private static readonly HashSet<string> _runContainers = ["hyperlink", "smartTag", "fldSimple", "ins", "moveTo", "sdt", "sdtContent", "customXml", "dir", "bdo"];

    public const string WORKBOOK_PART = "/xl/workbook.xml";
    public const string DOCUMENT_PART = "/word/document.xml";
    public const string DOCUMENT_RELATIONSHIPS = "/word/_rels/document.xml.rels";
    public const string DOCUMENT_FOLDER = "/word/";
    public const string WORKBOOK_TYPE = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
    public const string MACRO_WORKBOOK_TYPE = "application/vnd.ms-excel.sheet.macroEnabled.main+xml";
    public const string DOCUMENT_TYPE = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";
    public const string MACRO_DOCUMENT_TYPE = "application/vnd.ms-word.document.macroEnabled.main+xml";

    public static bool HasPart(byte[] package, string partName)
    {
        using ZipArchive zip = Open(package);
        return zip.GetEntry(partName.TrimStart('/')) is not null;
    }

    /// <summary>The part's content type as the package declares it: its own override, or else the default for its extension.</summary>
    public static string? ContentType(byte[] package, string partName)
    {
        XDocument types = Part(package, "/[Content_Types].xml");
        XElement? overrideType = types.Root!.Elements(_contentTypes + "Override")
            .FirstOrDefault(element => string.Equals((string?)element.Attribute("PartName"), partName, StringComparison.OrdinalIgnoreCase));
        XElement? defaultType = types.Root!.Elements(_contentTypes + "Default")
            .FirstOrDefault(element => string.Equals((string?)element.Attribute("Extension"), Path.GetExtension(partName).TrimStart('.'), StringComparison.OrdinalIgnoreCase));
        return (string?)(overrideType ?? defaultType)?.Attribute("ContentType");
    }

    public static XDocument Part(byte[] package, string partName)
    {
        using ZipArchive zip = Open(package);
        ZipArchiveEntry entry = zip.GetEntry(partName.TrimStart('/'))
            ?? throw new InvalidOperationException($"The package has no part {partName}.");
        using Stream stream = entry.Open();
        return XDocument.Load(stream);
    }

    public static IReadOnlyList<string> DefinedNames(byte[] workbook)
    {
        return [.. Part(workbook, WORKBOOK_PART).Descendants(SpreadsheetMl + "definedName").Select(name => (string)name.Attribute("name")!)];
    }

    public static string? SheetState(byte[] workbook, string sheetName)
    {
        XElement sheet = Part(workbook, WORKBOOK_PART).Descendants(SpreadsheetMl + "sheet")
            .Single(element => (string?)element.Attribute("name") == sheetName);
        return (string?)sheet.Attribute("state");
    }

    public static XDocument Worksheet(byte[] workbook, string sheetName)
    {
        XElement sheet = Part(workbook, WORKBOOK_PART).Descendants(SpreadsheetMl + "sheet")
            .Single(element => (string?)element.Attribute("name") == sheetName);
        string relationshipId = (string)sheet.Attribute(Relationships + "id")!;
        return Part(workbook, ResolveTarget(workbook, "/xl/_rels/workbook.xml.rels", "/xl/", relationshipId));
    }

    /// <summary>The runs of a cell's text, each with whether it is bold, whether the text is shared or inline.</summary>
    public static IReadOnlyList<(string Text, bool Bold)> TextRuns(byte[] workbook, string sheetName, string reference)
    {
        XElement cell = Worksheet(workbook, sheetName).Descendants(SpreadsheetMl + "c")
            .Single(element => (string?)element.Attribute("r") == reference);
        XElement text = (string?)cell.Attribute("t") == "s"
            ? SharedString(workbook, int.Parse(cell.Element(SpreadsheetMl + "v")!.Value, System.Globalization.CultureInfo.InvariantCulture))
            : cell.Element(SpreadsheetMl + "is")!;

        List<XElement> runs = [.. text.Elements(SpreadsheetMl + "r")];
        if (runs.Count == 0)
        {
            return [(text.Element(SpreadsheetMl + "t")!.Value, false)];
        }

        return [.. runs.Select(run => (
            run.Element(SpreadsheetMl + "t")!.Value,
            run.Element(SpreadsheetMl + "rPr")?.Element(SpreadsheetMl + "b") is { } bold && (string?)bold.Attribute("val") is null or "1" or "true"))];
    }

    public static IReadOnlyList<XElement> WordParagraphs(byte[] document, string partName = DOCUMENT_PART)
    {
        return [.. Part(document, partName).Descendants(WordprocessingMl + "p").Where(IsShown)];
    }

    /// <summary>Whether Word shows the element: content in the fallback of an alternate content block repeats its choice.</summary>
    public static bool IsShown(XElement element)
    {
        return !element.Ancestors(MarkupCompatibility + "Fallback").Any();
    }

    /// <summary>A paragraph's own text, with a line break as "\n"; text in a nested text box is left out.</summary>
    public static string ParagraphText(XElement paragraph)
    {
        return string.Concat(Runs(paragraph).Select(RunText));
    }

    /// <summary>
    /// The runs a paragraph shows, in document order, those inside its hyperlinks, fields, insertions, content controls
    /// and custom markup included. A text box inside a run keeps its runs to its own paragraphs.
    /// </summary>
    public static IEnumerable<XElement> Runs(XElement paragraph)
    {
        foreach (XElement child in paragraph.Elements())
        {
            if (child.Name == WordprocessingMl + "r")
            {
                yield return child;
            }
            else if (_runContainers.Contains(child.Name.LocalName) && child.Name.Namespace == WordprocessingMl)
            {
                foreach (XElement run in Runs(child))
                {
                    yield return run;
                }
            }
        }
    }

    /// <summary>A run's text, with a line break as "\n" and a tab as "\t".</summary>
    public static string RunText(XElement run)
    {
        var text = new StringBuilder();
        foreach (XElement content in run.Elements())
        {
            if (content.Name == WordprocessingMl + "t")
            {
                _ = text.Append(content.Value);
            }
            else if (content.Name == WordprocessingMl + "br" || content.Name == WordprocessingMl + "cr")
            {
                _ = text.Append('\n');
            }
            else if (content.Name == WordprocessingMl + "tab")
            {
                _ = text.Append('\t');
            }
        }

        return text.ToString();
    }

    public static string AllText(byte[] document)
    {
        return string.Join("\n", PartNames(document)
            .Where(name => name.StartsWith("word/", StringComparison.Ordinal) && name.EndsWith(".xml", StringComparison.Ordinal))
            .SelectMany(name => WordParagraphs(document, "/" + name))
            .Select(ParagraphText));
    }

    public static IReadOnlyList<XElement> Sections(byte[] document)
    {
        return [.. Part(document, DOCUMENT_PART).Descendants(WordprocessingMl + "sectPr")];
    }

    /// <summary>The footer a section shows on its pages, or null when it names none.</summary>
    public static XDocument? Footer(byte[] document, XElement section)
    {
        XElement? reference = section.Elements(WordprocessingMl + "footerReference")
            .FirstOrDefault(element => (string?)element.Attribute(WordprocessingMl + "type") == "default");
        return reference is null
            ? null
            : Part(document, ResolveTarget(document, DOCUMENT_RELATIONSHIPS, DOCUMENT_FOLDER, (string)reference.Attribute(Relationships + "id")!));
    }

    /// <summary>The text of each paragraph of a footer that shows any, in order.</summary>
    public static IReadOnlyList<string> FooterLines(XDocument footer)
    {
        return [.. footer.Descendants(WordprocessingMl + "p").Select(ParagraphText).Where(line => line.Length > 0)];
    }

    /// <summary>The field codes of a part, such as "PAGE", from complex and simple fields alike.</summary>
    public static IReadOnlyList<string> FieldCodes(XDocument part)
    {
        return [.. FieldInstructions(part.Root!)
            .Select(field => field.Instruction.Trim())
            .Where(code => code.Length > 0)
            .Select(code => code.Split(' ')[0])];
    }

    /// <summary>
    /// Each field under the element, with the element that starts it and its whole instruction, such as
    /// <c>HYPERLINK "https://example.com"</c>. A complex field's instruction joins every piece of field code between its
    /// begin and separate marks, however the writer split it across runs.
    /// </summary>
    public static IEnumerable<(XElement Start, string Instruction)> FieldInstructions(XElement root)
    {
        Stack<(XElement Start, StringBuilder? Code)> open = new();
        foreach (XElement element in root.Descendants())
        {
            if (element.Name == WordprocessingMl + "fldSimple")
            {
                yield return (element, (string?)element.Attribute(WordprocessingMl + "instr") ?? string.Empty);
            }
            else if (element.Name == WordprocessingMl + "instrText" && open.TryPeek(out (XElement Start, StringBuilder? Code) field) && field.Code is not null)
            {
                _ = field.Code.Append(element.Value);
            }
            else if (element.Name == WordprocessingMl + "fldChar")
            {
                string? mark = (string?)element.Attribute(WordprocessingMl + "fldCharType");
                if (mark == "begin")
                {
                    open.Push((element, new StringBuilder()));
                }
                else if (mark is "separate" or "end" && open.TryPop(out (XElement Start, StringBuilder? Code) closing))
                {
                    if (closing.Code is not null)
                    {
                        yield return (closing.Start, closing.Code.ToString());
                    }

                    if (mark == "separate")
                    {
                        open.Push((closing.Start, null));
                    }
                }
            }
        }
    }

    /// <summary>A twips attribute of a WordprocessingML element, or null when the element or the attribute is absent.</summary>
    public static int? Twips(XElement? element, string attribute)
    {
        return (string?)element?.Attribute(WordprocessingMl + attribute) is { } value
            ? int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)
            : null;
    }

    public static IReadOnlyList<string> PartNames(byte[] package)
    {
        using ZipArchive zip = Open(package);
        return [.. zip.Entries.Select(entry => entry.FullName)];
    }

    /// <summary>The relationships a part declares, or none when it has no relationships part.</summary>
    public static IReadOnlyList<(string Id, string Type, string Target)> PartRelationships(byte[] package, string relationshipsPart)
    {
        return HasPart(package, relationshipsPart)
            ? [.. Part(package, relationshipsPart).Root!.Elements(_packageRelationships + "Relationship")
                .Select(element => ((string)element.Attribute("Id")!, (string)element.Attribute("Type")!, (string)element.Attribute("Target")!))]
            : [];
    }

    /// <summary>The part a relationship of the given type points at, or null when there is none.</summary>
    public static string? FindTargetByType(IReadOnlyList<(string Id, string Type, string Target)> relationships, string baseFolder, string typeSuffix)
    {
        return relationships
            .Where(relationship => relationship.Type.EndsWith(typeSuffix, StringComparison.Ordinal))
            .Select(relationship => Combine(baseFolder, relationship.Target))
            .FirstOrDefault();
    }

    private static XElement SharedString(byte[] workbook, int index)
    {
        string partName = ResolveTargetByType(workbook, "/xl/_rels/workbook.xml.rels", "/xl/", "/sharedStrings");
        return Part(workbook, partName).Root!.Elements(SpreadsheetMl + "si").ElementAt(index);
    }

    private static string ResolveTarget(byte[] package, string relationshipsPart, string baseFolder, string relationshipId)
    {
        return Combine(baseFolder, PartRelationships(package, relationshipsPart).Single(relationship => relationship.Id == relationshipId).Target);
    }

    private static string ResolveTargetByType(byte[] package, string relationshipsPart, string baseFolder, string typeSuffix)
    {
        return FindTargetByType(PartRelationships(package, relationshipsPart), baseFolder, typeSuffix)
            ?? throw new InvalidOperationException($"The package has no {typeSuffix} relationship in {relationshipsPart}.");
    }

    private static string Combine(string baseFolder, string target)
    {
        return target.StartsWith('/') ? target : baseFolder + target;
    }

    private static ZipArchive Open(byte[] package)
    {
        return new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
    }
}

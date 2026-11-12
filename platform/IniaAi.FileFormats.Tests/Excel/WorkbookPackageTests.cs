using System.IO.Compression;
using System.Text;
using System.Xml;
using IniaAi.FileFormats.ContractTests;
using IniaAi.FileFormats.Excel;

namespace IniaAi.FileFormats.Tests.Excel;

/// <summary>The copy of a workbook that drops the parts a library fails to load never expands an entity a part declares.</summary>
public class WorkbookPackageTests
{
    private const string SHEET_PART = "xl/worksheets/sheet1.xml";
    private const string WORKBOOK_PART = "xl/workbook.xml";
    private const string NESTED_ENTITIES = $"<!ENTITY a \"{EntityPackages.ENTITY_TEXT}\"><!ENTITY b \"&a;&a;&a;\"><!ENTITY c \"&b;&b;&b;\">";

    [Fact]
    public void WithoutCommentsAndDrawings_OfASheetThatDeclaresEntities_RefusesIt_OrLeavesThemUnexpanded()
    {
        byte[] workbook = WithPart(SHEET_PART,
            $"<?xml version=\"1.0\"?><!DOCTYPE worksheet [{NESTED_ENTITIES}]><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>&c;</t></is></c></row></sheetData></worksheet>");

        string copied;
        try
        {
            using MemoryStream copy = WorkbookPackage.WithoutCommentsAndDrawings(new MemoryStream(workbook));
            using var package = new ZipArchive(copy, ZipArchiveMode.Read);
            using var reader = new StreamReader(package.GetEntry(SHEET_PART)!.Open());
            copied = reader.ReadToEnd();
        }
        catch (XmlException refusal)
        {
            copied = refusal.Message;
        }

        Assert.DoesNotContain(EntityPackages.EXPANDED_TEXT, copied);
    }

    [Fact]
    public void DefinedNames_OfAWorkbookThatDeclaresEntities_RefusesIt_OrLeavesThemUnexpanded()
    {
        byte[] workbook = WithPart(WORKBOOK_PART,
            $"<?xml version=\"1.0\"?><!DOCTYPE workbook [{NESTED_ENTITIES}]><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><definedNames><definedName name=\"Print_Area\">&c;</definedName></definedNames></workbook>");

        string names;
        try
        {
            names = string.Join('\n', WorkbookPackage.DefinedNames(new MemoryStream(workbook)).Select(name => name.Value));
        }
        catch (XmlException refusal)
        {
            names = refusal.Message;
        }

        Assert.DoesNotContain(EntityPackages.EXPANDED_TEXT, names);
    }

    private static byte[] WithPart(string part, string xml)
    {
        using var stream = new MemoryStream();
        stream.Write(ContractFixture.Bytes(ContractFixture.CELL_TEXTS_WORKBOOK));
        using (var package = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            package.GetEntry(part)!.Delete();
            using var writer = new StreamWriter(package.CreateEntry(part).Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(xml);
        }

        return stream.ToArray();
    }
}

using System.IO.Compression;
using System.Text;

namespace IniaAi.FileFormats.Tests.Excel;

internal static class BrokenWorkbooks
{
    private const string WORKBOOK_RELATIONSHIPS = "xl/_rels/workbook.xml.rels";
    private const string SHEET_RELATIONSHIPS_FOLDER = "xl/worksheets/_rels/";
    private const string PERSON_RELATIONSHIP =
        "<Relationship Id=\"rIdPerson\" Type=\"http://schemas.microsoft.com/office/2017/10/relationships/person\" Target=\"persons/person.xml\"/>";

    /// <summary>The workbook with a relationship to a people part it lacks, and a folder entry in its zip, as real uploads have.</summary>
    public static byte[] WithRelationshipToAMissingPeoplePart(byte[] workbook)
    {
        using var stream = new MemoryStream();
        stream.Write(workbook);
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            ZipArchiveEntry relationships = zip.GetEntry(WORKBOOK_RELATIONSHIPS)!;
            string xml;
            using (var reader = new StreamReader(relationships.Open()))
            {
                xml = reader.ReadToEnd();
            }

            relationships.Delete();
            _ = zip.CreateEntry(SHEET_RELATIONSHIPS_FOLDER);
            ZipArchiveEntry replaced = zip.CreateEntry(WORKBOOK_RELATIONSHIPS);
            using var writer = new StreamWriter(replaced.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(xml.Replace("</Relationships>", PERSON_RELATIONSHIP + "</Relationships>", StringComparison.Ordinal));
        }

        return stream.ToArray();
    }
}

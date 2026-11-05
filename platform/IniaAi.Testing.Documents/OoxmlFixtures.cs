using ClosedXML.Excel;
using IniaAi.Common.Resources;

namespace IniaAi.Testing.Documents;

/// <summary>
/// Workbook bytes for tests. ClosedXML writes the Office Open XML workbooks, and <see cref="OfficeEncryption"/>
/// encrypts one under a password. No free library writes an Excel 97-2003 workbook or a VBA project, so those come
/// from files that <c>tools/synthetic-fixtures</c> wrote. <see cref="WordFixtures"/> builds the Word documents.
/// </summary>
public static class OoxmlFixtures
{
    /// <summary>The password of <see cref="EncryptedLegacyWorkbook"/>.</summary>
    public const string LEGACY_WORKBOOK_PASSWORD = "legacy-pw-1";

    private const string DEFAULT_CELL = "A1";
    private const string DEFAULT_CELL_CONTENT = "cell";
    private const string SHEET_NAME = "Sheet1";
    private const string LEGACY_WORKBOOK = "legacy-workbook.xls";
    private const string ENCRYPTED_LEGACY_WORKBOOK = "legacy-workbook-encrypted.xls";
    private const string MACRO_WORKBOOK = "macro-workbook.xlsm";

    /// <summary>A workbook with "cell" in A1, encrypted when <paramref name="password"/> is given.</summary>
    public static byte[] Workbook(string? password = null)
    {
        byte[] workbook = WorkbookWithCells((DEFAULT_CELL, DEFAULT_CELL_CONTENT));
        return password is null ? workbook : OfficeEncryption.Encrypted(workbook, password);
    }

    public static byte[] WorkbookWithCells(params (string Cell, string Value)[] cells)
    {
        return BuiltWorkbook(workbook =>
        {
            IXLWorksheet sheet = workbook.AddWorksheet(SHEET_NAME);
            foreach ((string cell, string value) in cells)
            {
                sheet.Cell(cell).Value = value;
            }
        });
    }

    /// <summary>The saved bytes of a new workbook that <paramref name="build"/> fills.</summary>
    public static byte[] BuiltWorkbook(Action<XLWorkbook> build)
    {
        using var workbook = new XLWorkbook();
        build(workbook);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>An Excel 97-2003 workbook with "cell" in A1.</summary>
    public static byte[] LegacyWorkbook()
    {
        return Resource(LEGACY_WORKBOOK);
    }

    /// <summary>The same Excel 97-2003 workbook, encrypted under <see cref="LEGACY_WORKBOOK_PASSWORD"/>.</summary>
    public static byte[] EncryptedLegacyWorkbook()
    {
        return Resource(ENCRYPTED_LEGACY_WORKBOOK);
    }

    /// <summary>A macro-enabled workbook whose VBA project holds one module.</summary>
    public static byte[] MacroWorkbook()
    {
        return Resource(MACRO_WORKBOOK);
    }

    private static byte[] Resource(string name)
    {
        using Stream stream = typeof(OoxmlFixtures).Assembly.ReadResource(name);
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }
}

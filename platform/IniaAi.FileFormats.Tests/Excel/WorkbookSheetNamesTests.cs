using ClosedXML.Excel;
using IniaAi.FileFormats.Excel;
using IniaAi.Testing.Documents;

namespace IniaAi.FileFormats.Tests.Excel;

public class WorkbookSheetNamesTests
{
    [Fact]
    public void AStampedWorkbook_ListsEverySheet_HiddenAndVeryHiddenOnesIncluded()
    {
        byte[] workbook = OoxmlFixtures.BuiltWorkbook(built =>
        {
            _ = built.AddWorksheet("Financials LCY");
            built.AddWorksheet("Logs").Visibility = XLWorksheetVisibility.Hidden;
        });
        workbook = ExcelAnalysisConfigSheet.Stamp(
            new ClosedXmlEditableWorkbookFormat(),
            workbook,
            new ExcelAnalysisConfig(ExcelAnalysisConfigSheet.CURRENT_SCHEMA_VERSION, ExcelAnalysisSource.BlockGenerated));

        IReadOnlySet<string> names = WorkbookSheetNames.Read(new MemoryStream(workbook));

        Assert.Equal(["Financials LCY", "Logs", ExcelAnalysisConfigSheet.SHEET_NAME], names.Order(StringComparer.Ordinal));
        Assert.Contains("logs", names);
    }

    [Fact]
    public void BytesThatAreNotAWorkbookPackage_AreRefused()
    {
        _ = Assert.Throws<InvalidDataException>(() => WorkbookSheetNames.Read(new MemoryStream("%PDF-1.7"u8.ToArray())));
    }
}

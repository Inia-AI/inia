using System.Globalization;
using ClosedXML.Excel;
using IniaAi.FileFormats.ContractTests;
using IniaAi.FileFormats.Excel;
using IniaAi.FileFormats.Inspection;
using IniaAi.Testing.Documents;

namespace IniaAi.FileFormats.Tests.Excel;

public class ClosedXmlWorkbookFormatTests
{
    private const int SHORT_DATE = 14;
    private const int DATE_AND_TIME = 22;

    [Fact]
    public void Open_AWorkbookThatNamesAMissingPeoplePart_StillReadsItsSheets_WhenItsZipListsFolders()
    {
        byte[] workbook = BrokenWorkbooks.WithRelationshipToAMissingPeoplePart(ContractFixture.Bytes(ContractFixture.CELL_TEXTS_WORKBOOK));

        using IWorkbookDocument document = new ClosedXmlWorkbookFormat().Open(new MemoryStream(workbook), "people.xlsx");

        Assert.Equal("Values", document.Sheets[0].Name);
        Assert.Equal("42.5", document.Sheets[0].CellText(1, 1));
    }

    [Fact]
    public void Open_ALegacyWorkbook_FailsWithTheReasonTheOpenBuildRefusesIt()
    {
        InvalidDataException failure = Assert.Throws<InvalidDataException>(
            () => new ClosedXmlWorkbookFormat().Open(new MemoryStream(OoxmlFixtures.LegacyWorkbook()), "figures.xls"));

        Assert.Equal(OpenBuildFormats.LEGACY_WORKBOOK_NOT_READ, failure.Message);
    }

    [Theory]
    [InlineData(SHORT_DATE, "", "01/31/2020")]
    [InlineData(SHORT_DATE, "en-US", "1/31/2020")]
    [InlineData(SHORT_DATE, "en-GB", "31/01/2020")]
    [InlineData(DATE_AND_TIME, "", "01/31/2020 13:05")]
    [InlineData(DATE_AND_TIME, "en-US", "1/31/2020 13:05")]
    [InlineData(DATE_AND_TIME, "en-GB", "31/01/2020 13:05")]
    public void CellText_OfABuiltInDateThatExcelLocalizes_FollowsTheShortDateOfTheCulture(int formatId, string culture, string expected)
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            byte[] workbook = OoxmlFixtures.BuiltWorkbook(built =>
            {
                IXLCell cell = built.AddWorksheet("Dates").Cell("A1");
                cell.Value = new DateTime(2020, 1, 31, 13, 5, 0).ToOADate();
                cell.Style.NumberFormat.NumberFormatId = formatId;
            });
            using IWorkbookDocument document = new ClosedXmlWorkbookFormat().Open(new MemoryStream(workbook), "dates.xlsx");

            Assert.Equal(expected, document.Sheets[0].CellText(0, 0));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}

using IniaAi.FileFormats.ContractTests;
using IniaAi.FileFormats.Tests.Excel;
using IniaAi.FileFormats.Word.OpenXml;
using IniaAi.Testing.Ooxml;

namespace IniaAi.FileFormats.Tests.Word;

public class OpenXmlWorkbookToWordConverterTests
{
    [Fact]
    public void ConvertToWord_OfAWorkbookThatNamesAMissingPeoplePart_StillCopiesItsCells()
    {
        byte[] workbook = BrokenWorkbooks.WithRelationshipToAMissingPeoplePart(ContractFixture.Bytes(ContractFixture.CELL_TEXTS_WORKBOOK));

        string text = OoxmlPackage.AllText(new OpenXmlWorkbookToWordConverter().ConvertToWord(workbook));

        Assert.Contains("42.5", text, StringComparison.Ordinal);
    }
}

using IniaAi.FileFormats.ContractTests.Word;
using IniaAi.FileFormats.Word;
using IniaAi.FileFormats.Word.OpenXml;

namespace IniaAi.FileFormats.Tests.Contracts;

public class OpenXmlWorkbookToWordContractTests : WorkbookToWordContract
{
    protected override IWorkbookToWordConverter Converter { get; } = new OpenXmlWorkbookToWordConverter();
}

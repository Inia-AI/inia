using IniaAi.FileFormats.ContractTests.Excel;
using IniaAi.FileFormats.Excel;

namespace IniaAi.FileFormats.Tests.Contracts;

public class ClosedXmlWorkbookFormatContractTests : WorkbookFormatContract
{
    protected override IWorkbookFormat Format { get; } = new ClosedXmlWorkbookFormat();
}

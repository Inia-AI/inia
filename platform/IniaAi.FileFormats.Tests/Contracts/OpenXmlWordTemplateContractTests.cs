using IniaAi.FileFormats.ContractTests.Word;
using IniaAi.FileFormats.Word;
using IniaAi.FileFormats.Word.OpenXml;

namespace IniaAi.FileFormats.Tests.Contracts;

public class OpenXmlWordTemplateContractTests : WordTemplateContract
{
    protected override IWordTemplateFormat Format { get; } = new OpenXmlWordTemplateFormat();
}

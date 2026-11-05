using IniaAi.Configuration;
using IniaAi.FileFormats.ContractTests.Word;
using IniaAi.FileFormats.Word;
using IniaAi.FileFormats.Word.OpenXml;
using Microsoft.Extensions.Options;

namespace IniaAi.FileFormats.Tests.Contracts;

public class OpenXmlWordPageReaderContractTests : WordPageReaderContract
{
    protected override IWordPageReader Reader { get; } = new OpenXmlWordPageReader(Options.Create(new PlatformSettings()));
}

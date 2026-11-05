using IniaAi.FileFormats.ContractTests.Word;
using IniaAi.FileFormats.Word;
using IniaAi.FileFormats.Word.OpenXml;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.FileFormats.Tests.Contracts;

public class OpenXmlWordReportWriterContractTests : WordReportWriterContract
{
    protected override IReportFileWriter Writer { get; } = new OpenXmlWordReportWriter(NullLogger<OpenXmlWordReportWriter>.Instance);
}

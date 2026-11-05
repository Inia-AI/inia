using IniaAi.FileFormats.ContractTests.Word;
using IniaAi.FileFormats.Markdown;
using IniaAi.FileFormats.Word;

namespace IniaAi.FileFormats.Tests.Contracts;

public class MarkdownReportWriterContractTests : ReportFileWriterContract
{
    protected override IReportFileWriter Writer { get; } = new MarkdownReportWriter();
}

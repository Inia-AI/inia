using IniaAi.Common.Enums;
using IniaAi.Common.Interfaces;
using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using IniaAi.Domain.Enums;
using IniaAi.FileFormats.ContractTests;
using IniaAi.FileFormats.Excel;
using IniaAi.FileFormats.Inspection;
using IniaAi.FileFormats.Pdf;
using IniaAi.FileFormats.Pdf.Pdfium;
using IniaAi.FileFormats.Unlock;
using IniaAi.FileFormats.Word;
using IniaAi.FileFormats.Word.OpenXml;
using IniaAi.Testing;
using IniaAi.Testing.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IniaAi.FileFormats.Tests;

public class OpenFileFormatsServicesTests
{
    [Theory]
    [InlineData(typeof(IWorkbookFormat), typeof(ClosedXmlWorkbookFormat))]
    [InlineData(typeof(IEditableWorkbookFormat), typeof(ClosedXmlEditableWorkbookFormat))]
    [InlineData(typeof(IWordPageReader), typeof(OpenXmlWordPageReader))]
    [InlineData(typeof(IWorkbookToWordConverter), typeof(OpenXmlWorkbookToWordConverter))]
    [InlineData(typeof(IWordTemplateFormat), typeof(OpenXmlWordTemplateFormat))]
    [InlineData(typeof(IPdfFormat), typeof(PdfiumPdfFormat))]
    [InlineData(typeof(IDocumentUnlocker), typeof(OpenDocumentUnlocker))]
    [InlineData(typeof(IReadableFormats), typeof(OpenBuildFormats))]
    public void AddOpenFileFormats_RegistersTheOpenImplementation(Type service, Type implementation)
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddSingleton(Options.Create(new PlatformSettings()))
            .AddOpenFileFormats()
            .BuildServiceProvider();

        Assert.IsType(implementation, provider.GetRequiredService(service));
    }

    [Fact]
    public void AddOpenFileFormats_SizesThePdfWorkerLimitFromTheSetting()
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddSingleton(Options.Create(new PlatformSettings { PdfReader = new PdfReaderSettings { MaxWorkers = 3 } }))
            .AddOpenFileFormats()
            .BuildServiceProvider();

        Assert.Equal(3, provider.GetRequiredService<PdfiumWorkerLimit>().Limit);
    }

    [Fact]
    public async Task AddOpenFileFormats_ThePdfReaderAndTheUnlockerShareOneWorkerLimit()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using ServiceProvider provider = new ServiceCollection()
            .AddSingleton(Options.Create(new PlatformSettings { PdfReader = new PdfReaderSettings { MaxWorkers = 1 } }))
            .AddOpenFileFormats()
            .BuildServiceProvider();
        PdfiumWorkerLimit workers = provider.GetRequiredService<PdfiumWorkerLimit>();
        IPdfDocument open = provider.GetRequiredService<IPdfFormat>().Open(
            ContractFixture.Stream(ContractFixture.THREE_PAGES_PDF), ContractFixture.NameOf(ContractFixture.THREE_PAGES_PDF));
        byte[] encrypted = PdfFixtures.Encrypted(PdfFixtures.Plain("Locked page text"), "user-pw-1", "owner-pw-1");

        Task<FileUnlockResult> unlocking = provider.GetRequiredService<IDocumentUnlocker>()
            .TryUnlockAsync(encrypted, EncryptedContainer.Pdf, "user-pw-1", cancellationToken);
        await Eventually.UntilAsync(() => workers.Waiting == 1, "the unlock to wait for the reader's worker", cancellationToken: cancellationToken);
        open.Dispose();

        Assert.Equal(FileProtectionStatus.Decrypted, (await unlocking.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken)).Status);
    }

    [Fact]
    public async Task AddOpenFileFormats_CountsAnUploadsPdf_WhileReportsHoldEveryReader()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using ServiceProvider provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton(Options.Create(new PlatformSettings { PdfReader = new PdfReaderSettings { MaxWorkers = 1 } }))
            .AddOpenFileFormats()
            .BuildServiceProvider();
        using IPdfDocument heldByAReport = provider.GetRequiredService<IPdfFormat>().Open(
            ContractFixture.Stream(ContractFixture.THREE_PAGES_PDF), ContractFixture.NameOf(ContractFixture.THREE_PAGES_PDF));

        Task<int?> counting = Task.Run(
            () => provider.GetRequiredService<IDocumentPageCounter>().Count(
                ContractFixture.Bytes(ContractFixture.THREE_PAGES_PDF), MediaType.ApplicationPdf, "upload.pdf"),
            cancellationToken);

        Assert.Equal(3, await counting.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken));
    }

    [Fact]
    public void AddOpenFileFormats_WritesWordReportsWithTheOpenWriter()
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddLogging()
            .AddOpenFileFormats()
            .BuildServiceProvider();

        Assert.IsType<OpenXmlWordReportWriter>(ReportFileFormats.ByFormat(provider.GetServices<IReportFileWriter>())[ExportFormat.Word]);
    }
}

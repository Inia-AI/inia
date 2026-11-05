using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Common;
using IniaAi.Application.Common.Interfaces.Delegates;
using IniaAi.Application.Common.Services;
using IniaAi.Common.Content;
using IniaAi.Common.Enums;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Configuration.Storage;
using IniaAi.Core.Services;
using IniaAi.Core.Services.Models;
using IniaAi.Core.Workflows.Outputs;
using IniaAi.Core.Workflows.Outputs.Interfaces;
using IniaAi.FileFormats.Pdf;
using IniaAi.Services.Documents;
using IniaAi.Testing.Documents;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace IniaAi.Services.Tests.Documents;

public class DocumentsFixture : TestsBaseFixture<DocumentProcessingService>
{
    public BinaryContent ImageSample1 { get; }
    public BinaryContent ImageSample2 { get; }
    public BinaryContent PdfSample1 { get; }
    public BinaryContent PdfSample2 { get; }
    public BinaryContent PdfSample3 { get; }
    public BinaryContent DoublePagesPdfSample { get; }
    public BinaryContent LandscapeSinglePagePdfSample { get; }
    public BinaryContent MixedPagesPdfSample { get; }

    public DocumentsFixture()
    {

        ImageSample1 = new(BinaryData.FromBytes(File.ReadAllBytes("TestResources/ManagerQs1.png")), MediaType.ImagePng);
        ImageSample2 = new(BinaryData.FromBytes(File.ReadAllBytes("TestResources/ManagerQs2.png")), MediaType.ImagePng);

        PdfSample1 = new(BinaryData.FromBytes(File.ReadAllBytes("TestResources/MultiPageSearchable.pdf")), MediaType.ApplicationPdf);
        PdfSample2 = new(BinaryData.FromBytes(File.ReadAllBytes("TestResources/WithGraphicsSearchable.pdf")), MediaType.ApplicationPdf);
        PdfSample3 = new(BinaryData.FromBytes(File.ReadAllBytes("TestResources/Topics_10_Pages.pdf")), MediaType.ApplicationPdf);
        DoublePagesPdfSample = new(BinaryData.FromBytes(LayoutSamples.DoublePages()), MediaType.ApplicationPdf);
        LandscapeSinglePagePdfSample = new(BinaryData.FromBytes(LayoutSamples.LandscapeSinglePages()), MediaType.ApplicationPdf);
        MixedPagesPdfSample = new(BinaryData.FromBytes(LayoutSamples.MixedPages()), MediaType.ApplicationPdf);
    }

    internal IChatConnector GetChatConnector(ITestOutputHelper testOutputHelper)
    {
        return TestChatConnectorFactory.Create(
            PlatformSettings,
            TestsLogger.CreateLogger<IChatConnector>(testOutputHelper));
    }

    internal DocumentAnalysisService GetDocumentAnalysisService(
        ITestOutputHelper testOutputHelper,
        IChatConnector chatConnector)
    {
        return new DocumentAnalysisService(
            PlatformSettings,
            TestsLogger.CreateLogger<DocumentAnalysisService>(testOutputHelper),
            chatConnector,
            GetTenantModelFamilyResolver(testOutputHelper),
            new ServiceScopeFactory(),
            GetFileStorageManager(),
            FileFormatsUnderTest.Pdf);
    }

    internal ExcelSheetAnalysisService GetExcelSheetAnalysisService(
        ITestOutputHelper testOutputHelper,
        IServiceScopeFactory serviceScopeFactory,
        IChatConnector chatConnector)
    {
        return new ExcelSheetAnalysisService(
            chatConnector,
            TestsLogger.CreateLogger<ExcelSheetAnalysisService>(testOutputHelper),
            serviceScopeFactory,
            GetTenantModelFamilyResolver(testOutputHelper));
    }

    public OcrTranscriptionService GetOcrTranscriptionService(
        ITestOutputHelper testOutputHelper,
        IServiceScopeFactory serviceScopeFactory,
        IChatConnector chatConnector,
        IPdfFormat pdfFormat)
    {
        return new OcrTranscriptionService(
            TestsLogger.CreateLogger<OcrTranscriptionService>(testOutputHelper),
            chatConnector,
            serviceScopeFactory,
            pdfFormat,
            Options.Create(PlatformSettings));
    }

    internal IWorkflowOutputBuilder GetWorkflowOutputBuilder()
    {
        return new WorkflowOutputBuilder();
    }

    internal static IWorkflowOutputValidator GetWorkflowOutputValidator()
    {
        return new WorkflowOutputValidator();
    }

    internal IWorkflowOutputService GetWorkflowOutputService()
    {
        return new WorkflowOutputService(
            GetWorkflowOutputBuilder(),
            GetWorkflowOutputValidator(),
            Substitute.For<IServiceScopeFactory>(),
            NullLogger<WorkflowOutputService>.Instance);
    }

    internal static IFileStorageManager GetFileStorageManager()
    {
        // Create proper mock instances to avoid null reference exceptions
        IFileStorageResolver mockResolver = Substitute.For<IFileStorageResolver>();
        IOptions<FileStorageSettings> mockOptions = Substitute.For<IOptions<FileStorageSettings>>();
        _ = mockOptions.Value.Returns(new FileStorageSettings());

        return new FileStorageManager(mockResolver, mockOptions);
    }

    internal TenantModelFamilyResolver GetTenantModelFamilyResolver(ITestOutputHelper testOutputHelper)
    {
        return new TenantModelFamilyResolver(
            PlatformSettingsOptions,
            TestsLogger.CreateLogger<TenantModelFamilyResolver>(testOutputHelper));
    }
}

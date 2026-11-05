using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.AwsBedrock;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Common.Content;
using IniaAi.Common.Enums;
using IniaAi.Configuration;
using IniaAi.Configuration.Enums.Anthropic;
using IniaAi.Configuration.LLMProviders;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Services;
using IniaAi.Core.Tests.Documents;
using IniaAi.FileFormats.Images;
using IniaAi.FileFormats.Pdf;
using IniaAi.FileFormats.Pdf.Pdfium;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection.Mocks;
using IniaAi.Testing.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using System.Collections.Concurrent;

namespace IniaAi.Core.Tests.Services;

public class OcrTranscriptionServiceTests
{
    private const string TRANSCRIPTION_TEXT = "# Statement\n\n| Item | Value |\n| --- | --- |\n| Total | 5,400 |";
    private static readonly TimeSpan _workerWait = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task ProcessImageAsync_TranscribesWithSingleModelCallAndNoJudge()
    {
        IChatConnector connector = Substitute.For<IChatConnector>();
        StubChatResponse(connector, TRANSCRIPTION_TEXT);
        OcrTranscriptionService service = CreateService(connector);
        var pageImage = new BinaryContent(new byte[] { 0xFF, 0xD8, 0xFF }, MediaType.ImageJpeg);

        OcrTranscriptionService.OcrProcessingResult result = await service.ProcessImageAsync(
            pageImage,
            ModelFamily.Anthropic,
            cancellationToken: TestContext.Current.CancellationToken);

        _ = await connector.Received(1).GetResponseAsync(
            Arg.Any<RequestContentSequence>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            Arg.Any<bool>(),
            Arg.Any<string?>(),
            Arg.Any<ModelFamily?>(),
            Arg.Any<ModelEffort?>(),
            Arg.Any<CancellationToken>());
        Assert.True(result.Succeeded);
        Assert.False(result.NeedsReview);
        Assert.Equal(TRANSCRIPTION_TEXT, result.MarkdownResults);
    }

    [Fact]
    public async Task ProcessImageAsync_AttributesResultToTheGivenPageNumber()
    {
        IChatConnector connector = Substitute.For<IChatConnector>();
        StubChatResponse(connector, TRANSCRIPTION_TEXT);
        OcrTranscriptionService service = CreateService(connector);
        var pageImage = new BinaryContent(new byte[] { 0xFF, 0xD8, 0xFF }, MediaType.ImageJpeg);

        OcrTranscriptionService.OcrProcessingResult result = await service.ProcessImageAsync(
            pageImage,
            ModelFamily.Anthropic,
            pageNumber: 4,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(4, result.PageNumber);
    }

    [Fact]
    public async Task ProcessImageAsync_ContentFilteredTranscription_StillRoutesByImage()
    {
        IChatConnector connector = Substitute.For<IChatConnector>();
        StubChatResponse(connector, $"{IChatConnector.ERROR_PREFIX}Request blocked by content filtering policy");
        OcrTranscriptionService service = CreateService(connector);
        var pageImage = new BinaryContent(new byte[] { 0xFF, 0xD8, 0xFF }, MediaType.ImageJpeg);

        OcrTranscriptionService.OcrProcessingResult result = await service.ProcessImageAsync(
            pageImage,
            ModelFamily.Anthropic,
            cancellationToken: TestContext.Current.CancellationToken);

        _ = await connector.Received(1).GetResponseAsync(
            Arg.Any<RequestContentSequence>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            Arg.Any<bool>(),
            Arg.Any<string?>(),
            Arg.Any<ModelFamily?>(),
            Arg.Any<ModelEffort?>(),
            Arg.Any<CancellationToken>());
        Assert.False(result.Succeeded);
        Assert.True(result.NeedsReview);
        Assert.Null(result.MarkdownResults);
    }

    [Fact]
    public async Task ProcessImageAsync_TheModelRefusesThePage_FlagsItWithNoTranscription()
    {
        var connector = new ScriptedChatConnector(
            _ => "partial text before the refusal",
            _ => new ModelRefusal("general_harms", null, "msg_refused", "us.anthropic.claude-sonnet-5-5"));
        OcrTranscriptionService service = CreateScriptedService(connector);

        OcrTranscriptionService.OcrProcessingResult result = await service.ProcessImageAsync(
            ScannedPage(), ModelFamily.Anthropic, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(connector.Calls);
        Assert.True(result.NeedsReview);
        Assert.False(result.Succeeded);
        Assert.Null(result.MarkdownResults);
        Assert.Equal("general_harms", result.Refusal?.Category);
    }

    [Fact]
    public async Task ProcessImageAsync_TheModelWritesARefusal_FlagsThePageAndStoresNoText()
    {
        var connector = new ScriptedChatConnector(
            _ => "I can't transcribe this one. The image is an identity card with a person's photo, name and ID number.");
        var logger = new CapturingLogger<OcrTranscriptionService>();
        OcrTranscriptionService service = CreateScriptedService(connector, logger);

        OcrTranscriptionService.OcrProcessingResult result = await service.ProcessImageAsync(
            ScannedPage(), ModelFamily.Anthropic, cancellationToken: TestContext.Current.CancellationToken);

        _ = Assert.Single(logger.Entries, entry => entry.Message.StartsWith(ModelRefusal.LOG_MARKER, StringComparison.Ordinal));
        Assert.True(result.NeedsReview);
        Assert.Null(result.MarkdownResults);
        Assert.Equal(ModelRefusal.WRITTEN_CATEGORY, result.Refusal?.Category);
    }

    [Fact]
    public async Task ProcessImageAsync_AnIdCardTranscriptionThatSaysCannot_IsStoredAsText()
    {
        const string transcription = """
            # IDENTITY CARD

            | Field | Value |
            | --- | --- |
            | Surname | MUSTERMANN |
            | Given names | ERIKA |

            This card cannot be used as a travel document. Personal data is protected by law.
            """;
        OcrTranscriptionService service = CreateScriptedService(ScriptedChatConnector.Answering(transcription));

        OcrTranscriptionService.OcrProcessingResult result = await service.ProcessImageAsync(
            ScannedPage(), ModelFamily.Anthropic, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.False(result.NeedsReview);
        Assert.Null(result.Refusal);
        Assert.Contains("MUSTERMANN", result.MarkdownResults);
    }

    [Theory]
    [InlineData(595, 842, false)]
    [InlineData(595, 842, true)]
    [InlineData(2592, 842, false)]
    public async Task ProcessPdf_RendersAtTheLongEdgeTheTranscribingModelReads(double widthPt, double heightPt, bool highResolution)
    {
        VisionTier tier = highResolution ? VisionTier.HighResolution : VisionTier.Standard;

        PictureSize rendered = await RenderSinglePagePdfAsync(widthPt, heightPt, tier);

        int wholeDpiRounding = (int)Math.Ceiling(Math.Max(widthPt, heightPt) / 72);
        Assert.InRange(Math.Max(rendered.Width, rendered.Height), tier.LongEdgePx - wholeDpiRounding, tier.LongEdgePx);
    }

    [Fact]
    public async Task ProcessPdf_ByDefault_TranscribesWithTheAdvancedModel()
    {
        await AssertProcessPdfTranscribesWithAsync(
            new OcrSettings(),
            expectedShouldUseAdvancedModel: true,
            expectedModel: null);
    }

    [Theory]
    [InlineData(false, null, null)]
    [InlineData(true, "claude-sonnet-4-6", "claude-sonnet-4-6")]
    [InlineData(false, "claude-opus-5", "claude-opus-5")]
    [InlineData(true, " ", null)]
    public async Task ProcessPdf_TranscribesWithTheModelTheOcrSettingsName(
        bool shouldUseAdvancedModel,
        string? modelIdOverride,
        string? expectedModel)
    {
        await AssertProcessPdfTranscribesWithAsync(
            new OcrSettings { ShouldUseAdvancedModel = shouldUseAdvancedModel, ModelIdOverride = modelIdOverride },
            shouldUseAdvancedModel,
            expectedModel);
    }

    [Fact]
    public async Task ProcessPdf_AModelOverrideTheDeploymentCannotCall_FailsBeforeAnyModelCall()
    {
        const string unknownModel = "gateway-sonnet";
        var settings = new PlatformSettings
        {
            Anthropic = new AnthropicSettings { AdvancedModel = AiModel.UsAwsSonnet5, BasicModel = AiModel.UsAwsHaiku5_5 },
            OcrSettings = new OcrSettings { ModelIdOverride = unknownModel },
        };
        var connector = new AwsBedrockChatProvider(
            settings,
            NullLogger<AwsBedrockChatProvider>.Instance,
            NullLoggerFactory.Instance,
            new NoOpLimiter());
        OcrTranscriptionService service = CreateService(connector, settings);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ProcessPdf(
            PdfFixtures.SizedPage(595, 842),
            "page.pdf",
            [1],
            ModelFamily.Anthropic,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(unknownModel, error.Message);
    }

    [Fact]
    public async Task ProcessPdf_ClosesItsDocumentWhenTheRenderingEnds_BeforeTheModelAnswers()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var oneWorker = new PdfiumPdfFormat(new PdfiumWorkerLimit(1));
        IChatConnector connector = Substitute.For<IChatConnector>();
        var modelAsked = new TaskCompletionSource();
        var modelAnswer = new TaskCompletionSource<ResponseContentSequence>();
        _ = connector.GetResponseAsync(
            Arg.Any<RequestContentSequence>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            Arg.Any<bool>(),
            Arg.Any<string?>(),
            Arg.Any<ModelFamily?>(),
            Arg.Any<ModelEffort?>(),
            Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                modelAsked.TrySetResult();
                return modelAnswer.Task;
            });
        OcrTranscriptionService service = CreateService(connector, VisionTier.Standard, oneWorker);

        Task<ConcurrentDictionary<int, OcrTranscriptionService.OcrProcessingResult>> transcribing = service.ProcessPdf(
            PdfFixtures.SizedPage(595, 842), "page.pdf", [1], ModelFamily.Anthropic, cancellationToken: cancellationToken);
        try
        {
            await modelAsked.Task.WaitAsync(_workerWait, cancellationToken);

            using IPdfDocument other = await Task.Run(
                () => oneWorker.Open(new MemoryStream(PdfFixtures.SizedPage(595, 842)), "other.pdf"),
                cancellationToken).WaitAsync(_workerWait, cancellationToken);

            Assert.Equal(1, other.PageCount);
        }
        finally
        {
            var response = new ResponseContentSequence();
            response.Add(new ResponseCommonContent(TRANSCRIPTION_TEXT));
            modelAnswer.TrySetResult(response);
            _ = await transcribing.WaitAsync(_workerWait, cancellationToken);
        }
    }

    [Fact]
    public void ResolveRenderParallelism_Unset_DefaultsToSerial()
    {
        Assert.Equal(1, OcrTranscriptionService.ResolveRenderParallelism(null));
        Assert.Equal(1, OcrTranscriptionService.ResolveRenderParallelism(new OcrSettings()));
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("8", 8)]
    [InlineData("24", 24)]
    public void ResolveRenderParallelism_PositiveConfiguredValue_IsHonored(string configured, int expected)
    {
        var settings = new OcrSettings { OcrMaxDegreeOfParallelism = configured };

        Assert.Equal(expected, OcrTranscriptionService.ResolveRenderParallelism(settings));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("abc")]
    [InlineData("")]
    public void ResolveRenderParallelism_UnusableConfiguredValue_FallsBackToSerial(string configured)
    {
        var settings = new OcrSettings { OcrMaxDegreeOfParallelism = configured };

        Assert.Equal(1, OcrTranscriptionService.ResolveRenderParallelism(settings));
    }

    [Fact]
    public void ResolveLlmParallelism_Unset_DefaultsToHalfProcessorCount()
    {
        int expected = Math.Max(1, Environment.ProcessorCount / 2);

        Assert.Equal(expected, OcrTranscriptionService.ResolveLlmParallelism(null));
        Assert.Equal(expected, OcrTranscriptionService.ResolveLlmParallelism(new OcrSettings()));
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("12", 12)]
    public void ResolveLlmParallelism_PositiveConfiguredValue_IsHonored(string configured, int expected)
    {
        var settings = new OcrSettings { LlmMaxDegreeOfParallelism = configured };

        Assert.Equal(expected, OcrTranscriptionService.ResolveLlmParallelism(settings));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("garbage")]
    public void ResolveLlmParallelism_UnusableConfiguredValue_FallsBackToDefault(string configured)
    {
        var settings = new OcrSettings { LlmMaxDegreeOfParallelism = configured };

        Assert.Equal(
            Math.Max(1, Environment.ProcessorCount / 2),
            OcrTranscriptionService.ResolveLlmParallelism(settings));
    }

    private static OcrTranscriptionService CreateScriptedService(
        ScriptedChatConnector connector, ILogger<OcrTranscriptionService>? logger = null)
    {
        ServiceProvider provider = new ServiceCollection().BuildServiceProvider();

        return new OcrTranscriptionService(
            logger ?? NullLogger<OcrTranscriptionService>.Instance,
            connector,
            provider.GetRequiredService<IServiceScopeFactory>(),
            FileFormatsUnderTest.Pdf,
            Options.Create(new PlatformSettings()));
    }

    private static BinaryContent ScannedPage()
    {
        return new BinaryContent(new byte[] { 0xFF, 0xD8, 0xFF }, MediaType.ImageJpeg);
    }

    private static async Task AssertProcessPdfTranscribesWithAsync(
        OcrSettings ocrSettings,
        bool expectedShouldUseAdvancedModel,
        string? expectedModel)
    {
        IChatConnector connector = Substitute.For<IChatConnector>();
        StubChatResponse(connector, TRANSCRIPTION_TEXT);
        _ = connector.GetVisionTier(Arg.Any<bool>(), Arg.Any<string?>()).Returns(VisionTier.Standard);
        OcrTranscriptionService service = CreateService(connector, new PlatformSettings { OcrSettings = ocrSettings });

        _ = await service.ProcessPdf(
            PdfFixtures.SizedPage(595, 842),
            "page.pdf",
            [1],
            ModelFamily.Anthropic,
            cancellationToken: TestContext.Current.CancellationToken);

        _ = connector.Received().GetVisionTier(
            expectedShouldUseAdvancedModel,
            Arg.Is<string?>(model => model == expectedModel));
        _ = await connector.Received(1).GetResponseAsync(
            Arg.Any<RequestContentSequence>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            expectedShouldUseAdvancedModel,
            Arg.Is<string?>(model => model == expectedModel),
            Arg.Any<ModelFamily?>(),
            Arg.Any<ModelEffort?>(),
            Arg.Any<CancellationToken>());
    }

    private static OcrTranscriptionService CreateService(IChatConnector connector, VisionTier? visionTier = null, IPdfFormat? pdfFormat = null)
    {
        _ = connector.GetVisionTier(Arg.Any<bool>(), Arg.Any<string?>()).Returns(visionTier ?? VisionTier.Standard);

        return CreateService(connector, new PlatformSettings(), pdfFormat);
    }

    private static OcrTranscriptionService CreateService(IChatConnector connector, PlatformSettings settings, IPdfFormat? pdfFormat = null)
    {
        ServiceProvider provider = new ServiceCollection().BuildServiceProvider();

        return new OcrTranscriptionService(
            NullLogger<OcrTranscriptionService>.Instance,
            connector,
            provider.GetRequiredService<IServiceScopeFactory>(),
            pdfFormat ?? FileFormatsUnderTest.Pdf,
            Options.Create(settings));
    }

    private static void StubChatResponse(
        IChatConnector connector,
        string responseText,
        List<RequestContentSequence>? requests = null)
    {
        var response = new ResponseContentSequence
        {
            InputTokens = 10,
            OutputTokens = 20,
        };
        response.Add(new ResponseCommonContent(responseText));

        _ = connector.GetResponseAsync(
            Arg.Do<RequestContentSequence>(sequence => requests?.Add(sequence)),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            Arg.Any<bool>(),
            Arg.Any<string?>(),
            Arg.Any<ModelFamily?>(),
            Arg.Any<ModelEffort?>(),
            Arg.Any<CancellationToken>())
            .Returns(response);
    }

    private static async Task<PictureSize> RenderSinglePagePdfAsync(double widthPt, double heightPt, VisionTier visionTier)
    {
        IChatConnector connector = Substitute.For<IChatConnector>();
        List<RequestContentSequence> requests = [];
        StubChatResponse(connector, TRANSCRIPTION_TEXT, requests);
        OcrTranscriptionService service = CreateService(connector, visionTier);
        byte[] pdfBytes = PdfFixtures.SizedPage(widthPt, heightPt);

        ConcurrentDictionary<int, OcrTranscriptionService.OcrProcessingResult> results = await service.ProcessPdf(
            pdfBytes,
            "page.pdf",
            [1],
            ModelFamily.Anthropic,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(results[1].Succeeded);

        PictureSize? rendered = Pictures.ReadSize(CapturedPageImage(requests).BinaryData.ToArray());
        Assert.NotNull(rendered);
        return rendered;
    }

    private static BinaryContent CapturedPageImage(List<RequestContentSequence> requests)
    {
        RequestContentSequence request = Assert.Single(requests);
        RequestCommonContent imageContent = Assert.Single(request, content => content.BinaryContent is not null);

        return imageContent.BinaryContent!;
    }
}

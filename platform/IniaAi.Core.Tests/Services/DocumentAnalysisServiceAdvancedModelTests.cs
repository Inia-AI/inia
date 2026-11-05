using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.AwsBedrock;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Common.Content;
using IniaAi.Common.Enums;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Configuration.Enums.Common;
using IniaAi.Core.Services;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Enums;
using IniaAi.Testing.Documents;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection;
using NSubstitute;

namespace IniaAi.Core.Tests.Services;

/// <summary>
/// Verifies that DocumentAnalysisService honours the overview prompt's
/// ShouldUseAdvancedModel flag when generating page overviews, and falls back
/// to the global SummarizationModelType setting when no flag is supplied.
/// </summary>
public class DocumentAnalysisServiceAdvancedModelTests : IClassFixture<DocumentsFixture>
{
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly DocumentsFixture _fixture;
    private readonly IChatConnector _chatConnector;
    private readonly IFileStorageManager _fileStorageManager;

    public DocumentAnalysisServiceAdvancedModelTests(
        DocumentsFixture fixture,
        ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
        _fixture = fixture;
        _chatConnector = Substitute.For<IChatConnector>();
        _fileStorageManager = Substitute.For<IFileStorageManager>();
    }

    [Fact]
    public async Task GenerateOverviewsAsync_PromptShouldUseAdvancedModelFalse_OverridesGlobalAdvancedSetting()
    {
        // Arrange — global is Advanced, the prompt says false: the prompt must win
        _fixture.PlatformSettings.IniaAi.SummarizationModelType = ModelTier.Advanced;
        StubChat(responseText: "<answer>Page 1: summary</answer>");

        Document document = CreateSinglePageDocument();

        // Act
        await BuildService().GenerateOverviewsAsync(
            document,
            "org-1",
            classPrompts: null,
            Prompts(shouldUseAdvancedModel: false),
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert — connector called with shouldUseAdvancedModel: false
        _ = await _chatConnector.Received(1).GetResponseAsync(
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            shouldUseAdvancedModel: false,
            Arg.Any<string?>(),
            Arg.Any<ModelFamily?>(),
            Arg.Any<ModelEffort?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateOverviewsAsync_PromptShouldUseAdvancedModelTrue_OverridesGlobalStandardSetting()
    {
        // Arrange — global is Basic, the prompt says true: the prompt must win
        _fixture.PlatformSettings.IniaAi.SummarizationModelType = ModelTier.Basic;
        StubChat(responseText: "<answer>Page 1: summary</answer>");

        Document document = CreateSinglePageDocument();

        // Act
        await BuildService().GenerateOverviewsAsync(
            document,
            "org-1",
            classPrompts: null,
            Prompts(shouldUseAdvancedModel: true),
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert — connector called with shouldUseAdvancedModel: true
        _ = await _chatConnector.Received(1).GetResponseAsync(
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            shouldUseAdvancedModel: true,
            Arg.Any<string?>(),
            Arg.Any<ModelFamily?>(),
            Arg.Any<ModelEffort?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateOverviewsSlidingWindowAsync_NullFlag_FallsBackToGlobalSetting()
    {
        // Arrange — global is Advanced, no per-call override: global wins
        _fixture.PlatformSettings.IniaAi.SummarizationModelType = ModelTier.Advanced;
        StubChat(responseText: "<answer>Page 1: summary</answer>");

        DocumentAnalysisService service = BuildService();
        Document document = CreateSinglePageDocument();

        // Act — call the public sliding-window method directly, leaving the
        // new flag unset (null) so the fallback branch executes
        await service.GenerateOverviewsSlidingWindowAsync(
            [.. document.Pages],
            organizationId: "org-1",
            systemPrompt: "sys",
            userPromptTemplate: "user {0}",
            cancellationToken: TestContext.Current.CancellationToken);

        _ = await _chatConnector.Received(1).GetResponseAsync(
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            shouldUseAdvancedModel: true,
            Arg.Any<string?>(),
            Arg.Any<ModelFamily?>(),
            Arg.Any<ModelEffort?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateOverviewsSlidingWindowAsync_ImageOnlyPage_GeneratesOverviewFromImage()
    {
        // Arrange — an image-only page (OCR judged unreliable, Text null) must be
        // summarized from its image at the resolved overview tier, not skipped
        _fixture.PlatformSettings.IniaAi.SummarizationModelType = ModelTier.Advanced;
        StubChatImage(responseText: "<answer>Image overview</answer>");

        var document = new Document(new FileMetadata("org-1", "scan.pdf", [0]), MediaType.ApplicationPdf);
        var page = new Page(document, 1, 1)
        {
            Status = PageStatus.OCRNeedsReview,
            Image = new BinaryContent(new byte[] { 1, 2, 3 }, MediaType.ImagePng)
        };

        // Act
        await BuildService().GenerateOverviewsSlidingWindowAsync(
            [.. document.Pages],
            organizationId: "org-1",
            systemPrompt: "sys",
            userPromptTemplate: "user {0}",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert — the image (content) overload was used at the resolved
        // advanced tier and produced the overview
        _ = await _chatConnector.Received(1).GetResponseAsync(
            Arg.Any<RequestContentSequence>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            shouldUseAdvancedModel: true,
            Arg.Any<string?>(),
            Arg.Any<ModelFamily?>(),
            Arg.Any<ModelEffort?>(),
            Arg.Any<CancellationToken>());
        Assert.Equal("Image overview", page.Overview);
    }

    [Fact]
    public async Task GenerateOverviewsSlidingWindowAsync_ImageOnlyNeighbour_DoesNotCorruptTextWindow()
    {
        // Arrange — page 2 is image-only; its empty text must not leak an empty
        // <page2> block into page 1's sliding-window context
        _fixture.PlatformSettings.IniaAi.SummarizationModelType = ModelTier.Basic;
        StubChat(responseText: "<answer>summary</answer>");
        StubChatImage(responseText: "<answer>img</answer>");

        var document = new Document(new FileMetadata("org-1", "multi.pdf", [0]), MediaType.ApplicationPdf);
        _ = new Page(document, 1, 1) { Text = "alpha" };
        _ = new Page(document, 2, 2)
        {
            Status = PageStatus.OCRNeedsReview,
            Image = new BinaryContent(new byte[] { 1, 2, 3 }, MediaType.ImagePng)
        };
        _ = new Page(document, 3, 3) { Text = "gamma" };

        // Act
        await BuildService().GenerateOverviewsSlidingWindowAsync(
            [.. document.Pages],
            organizationId: "org-1",
            systemPrompt: "sys",
            userPromptTemplate: "user {0}",
            contextWindow: 1,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert — page 1's text window holds its own block but not the
        // image-only neighbour's empty block
        _ = await _chatConnector.Received(1).GetResponseAsync(
            Arg.Is<string>(s => s.Contains("<page1>") && !s.Contains("<page2>")),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            Arg.Any<bool>(),
            Arg.Any<string?>(),
            Arg.Any<ModelFamily?>(),
            Arg.Any<ModelEffort?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateOverviewsSlidingWindowAsync_ImageOnlyPage_ImageUnavailable_SkipsWithoutOverview()
    {
        // Arrange — an image-only page whose image cannot be rehydrated (no
        // stored file) must be skipped: no model call, no overview
        _fixture.PlatformSettings.IniaAi.SummarizationModelType = ModelTier.Basic;
        StubChatImage(responseText: "<answer>unused</answer>");

        var document = new Document(new FileMetadata("org-1", "scan.pdf", [0]), MediaType.ApplicationPdf);
        var page = new Page(document, 1, 1) { Status = PageStatus.OCRNeedsReview };

        // Act
        await BuildService().GenerateOverviewsSlidingWindowAsync(
            [.. document.Pages],
            organizationId: "org-1",
            systemPrompt: "sys",
            userPromptTemplate: "user {0}",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert — the image was unavailable, so no request was issued
        _ = await _chatConnector.DidNotReceive().GetResponseAsync(
            Arg.Any<RequestContentSequence>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            Arg.Any<bool>(),
            Arg.Any<string?>(),
            Arg.Any<ModelFamily?>(),
            Arg.Any<ModelEffort?>(),
            Arg.Any<CancellationToken>());
        Assert.Null(page.Overview);
    }

    private DocumentAnalysisService BuildService()
    {
        return new DocumentAnalysisService(
            _fixture.PlatformSettings,
            TestsLogger.CreateLogger<DocumentAnalysisService>(_testOutputHelper),
            _chatConnector,
            _fixture.GetTenantModelFamilyResolver(_testOutputHelper),
            new ServiceScopeFactory(),
            _fileStorageManager,
            FileFormatsUnderTest.Pdf);
    }

    private static Document CreateSinglePageDocument()
    {
        var document = new Document(new FileMetadata("org-1", "test.pdf", [0]), MediaType.ApplicationPdf);
        _ = new Page(document, 1, 1) { Text = "Some page text" };
        return document;
    }

    private static PageOverviewPrompts Prompts(bool shouldUseAdvancedModel)
    {
        return new PageOverviewPrompts
        {
            Pages = new PageOverviewPrompt { SystemPrompt = "sys", UserPrompt = "user {0}", ShouldUseAdvancedModel = shouldUseAdvancedModel },
        };
    }

    private void StubChat(string responseText)
    {
        var response = new ResponseContentSequence();
        response.Add(new ResponseCommonContent(responseText));

        _ = _chatConnector.GetResponseAsync(
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            Arg.Any<bool>(),
            Arg.Any<string?>(),
            Arg.Any<ModelFamily?>(),
            Arg.Any<ModelEffort?>(),
            Arg.Any<CancellationToken>())
            .Returns(response);
    }

    private void StubChatImage(string responseText)
    {
        var response = new ResponseContentSequence();
        response.Add(new ResponseCommonContent(responseText));

        _ = _chatConnector.GetResponseAsync(
            Arg.Any<RequestContentSequence>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            Arg.Any<bool>(),
            Arg.Any<string?>(),
            Arg.Any<ModelFamily?>(),
            Arg.Any<ModelEffort?>(),
            Arg.Any<CancellationToken>())
            .Returns(response);
    }
}

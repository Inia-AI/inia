using IniaAi.Common.Enums;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Core.Services;
using IniaAi.Domain.DocumentAnalysis;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Enums;
using IniaAi.Testing.Documents;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection;
using IniaAi.Testing.DependencyInjection.Mocks;
using NSubstitute;

namespace IniaAi.Core.Tests.Services;

public class DocumentAnalysisServiceTests : IClassFixture<DocumentsFixture>
{
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly DocumentsFixture _fixture;
    private readonly DocumentAnalysisService _documentAnalyzer;

    public DocumentAnalysisServiceTests(
        DocumentsFixture fixture,
        ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
        _fixture = fixture;
        _documentAnalyzer = new DocumentAnalysisService(
            _fixture.PlatformSettings,
            TestsLogger.CreateLogger<DocumentAnalysisService>(_testOutputHelper),
            ScriptedChatConnector.Answering("<answer>An overview of the page.</answer>"),
            _fixture.GetTenantModelFamilyResolver(_testOutputHelper),
            new ServiceScopeFactory(),
            DocumentsFixture.GetFileStorageManager(),
            FileFormatsUnderTest.Pdf);
    }

    [Fact]
    public void GenerateTocUsingModelToc_WithModel_GeneratesTableOfContentsInAccordanceWithModel()
    {
        // Arrange
        var document = new Document(new FileMetadata("1234", "Reunion.pdf", [0]), MediaType.ApplicationPdf);

        _ = new Page(document, 1, 1)
        {
            Text = "Introduction\n\nChapter 1\n\nHi there",
            SectionTitles = "Introduction, Chapter 1"
        };
        _ = new Page(document, 2, 2);
        _ = new Page(document, 3, 3)
        {
            Text = "Chapter 2\n\nHithere\n\nDid you know?\n\nPeople are awesome",
            SectionTitles = "Chapter 2, Did you know?"
        };
        _ = new Page(document, 4, 4)
        {
            Text = "Chapter 3\n\nAlmost done\n\nConclusion\n\nThe end",
            SectionTitles = "Chapter 3, Conclusion"
        };

        string modelToc =
        """
        Introduction
        Chapter 1
        Chapter 2
        Chapter 3
        """;

        //Act
        _documentAnalyzer.GenerateTocUsingModelToc(document, modelToc);

        // Assert
        Assert.NotNull(document.TableOfContents);
        Assert.NotEmpty(document.TableOfContents);
        Assert.Contains("Introduction: 1", document.TableOfContents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Chapter 1: 1", document.TableOfContents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Chapter 2: 3", document.TableOfContents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Chapter 3: 4", document.TableOfContents, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Did you know?", document.TableOfContents, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Conclusion", document.TableOfContents, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateOverviewsAsync_WhenPromptHashMatches_SkipsRegeneration()
    {
        // Arrange
        string currentHash = DocumentAnalysisService.ComputeOverviewPromptHash(
            _standardPrompts.Pages!.SystemPrompt, _standardPrompts.Pages.UserPrompt);

        var document = new Document(new FileMetadata("TestOrg", "test.pdf", [0]), MediaType.ApplicationPdf)
        {
            OverviewPromptHash = currentHash
        };
        var page1 = new Page(document, 1, 1) { Text = "Some text", Overview = "Existing overview page 1" };
        page1.EmbeddingVector = new EmbeddingVector("test-model", [1.0, 2.0]);
        var page2 = new Page(document, 2, 2) { Text = "More text", Overview = "Existing overview page 2" };
        page2.EmbeddingVector = new EmbeddingVector("test-model", [3.0, 4.0]);

        // Act
        await _documentAnalyzer.GenerateOverviewsAsync(
            document,
            "TestOrg",
            classPrompts: null,
            workflowDefault: _standardPrompts,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert — overviews and embeddings unchanged, hash unchanged, no orphans
        Assert.Equal("Existing overview page 1", document[1]?.Overview);
        Assert.Equal("Existing overview page 2", document[2]?.Overview);
        Assert.NotNull(document[1]?.EmbeddingVector);
        Assert.NotNull(document[2]?.EmbeddingVector);
        Assert.Equal(currentHash, document.OverviewPromptHash);
        Assert.Empty(_documentAnalyzer.OrphanedEmbeddingVectors);
    }

    [Fact]
    public async Task GenerateOverviewsAsync_WhenPromptHashMismatch_ClearsAndRegenerates()
    {
        // Arrange
        var document = new Document(new FileMetadata("TestOrg", "Reunion.pdf", [0]), MediaType.ApplicationPdf)
        {
            OverviewPromptHash = "STALE_HASH"
        };
        var page1 = new Page(document, 1, 1) { Text = DOC_1_PAGE_1_TEXT, Overview = "Old overview 1" };
        page1.EmbeddingVector = new EmbeddingVector("test-model", [1.0, 2.0]);
        var page2 = new Page(document, 2, 2) { Text = DOC_1_PAGE_2_TEXT, Overview = "Old overview 2" };
        page2.EmbeddingVector = new EmbeddingVector("test-model", [3.0, 4.0]);

        // Act
        await _documentAnalyzer.GenerateOverviewsAsync(
            document,
            "TestOrg",
            classPrompts: null,
            workflowDefault: _standardPrompts,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert — overviews replaced with new LLM-generated values, embeddings cleared
        Assert.NotEqual("Old overview 1", document[1]?.Overview);
        Assert.NotEqual("Old overview 2", document[2]?.Overview);
        Assert.False(string.IsNullOrWhiteSpace(document[1]?.Overview));
        Assert.False(string.IsNullOrWhiteSpace(document[2]?.Overview));
        Assert.Null(document[1]?.EmbeddingVector);
        Assert.Null(document[2]?.EmbeddingVector);
        Assert.NotEqual("STALE_HASH", document.OverviewPromptHash);

        // Assert — orphaned vectors collected for DB cleanup
        Assert.Equal(2, _documentAnalyzer.OrphanedEmbeddingVectors.Count);
    }

    [Fact]
    public async Task GenerateOverviewsAsync_WhenPromptHashIsNull_RegeneratesOverviews()
    {
        // Arrange
        var document = new Document(new FileMetadata("TestOrg", "Reunion.pdf", [0]), MediaType.ApplicationPdf);
        // OverviewPromptHash is null by default
        _ = new Page(document, 1, 1) { Text = DOC_1_PAGE_1_TEXT, Overview = "Old overview" };

        // Act
        await _documentAnalyzer.GenerateOverviewsAsync(
            document,
            "TestOrg",
            classPrompts: null,
            workflowDefault: _standardPrompts,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert — overview replaced, hash stored
        Assert.NotEqual("Old overview", document[1]?.Overview);
        Assert.False(string.IsNullOrWhiteSpace(document[1]?.Overview));
        Assert.NotNull(document.OverviewPromptHash);
        Assert.NotEmpty(document.OverviewPromptHash);
    }

    [Fact]
    public async Task GenerateOverviewsAsync_AfterGeneration_StoresCorrectHash()
    {
        // Arrange
        string expectedHash = DocumentAnalysisService.ComputeOverviewPromptHash(
            _standardPrompts.Pages!.SystemPrompt, _standardPrompts.Pages.UserPrompt);

        var document = new Document(new FileMetadata("TestOrg", "Reunion.pdf", [0]), MediaType.ApplicationPdf);
        _ = new Page(document, 1, 1) { Text = DOC_1_PAGE_1_TEXT };

        // Act
        await _documentAnalyzer.GenerateOverviewsAsync(
            document,
            "TestOrg",
            classPrompts: null,
            workflowDefault: _standardPrompts,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expectedHash, document.OverviewPromptHash);
    }

    [Fact]
    public async Task GenerateOverviewsAsync_UsesTheClassPrompt_OverTheWorkflowDefault()
    {
        ScriptedChatConnector model = ScriptedChatConnector.Answering("<answer>An overview of the page.</answer>");
        Document document = TwoPagePdf();

        await AnalyzerAnswering(model).GenerateOverviewsAsync(
            document,
            "TestOrg",
            Prompts("class system"),
            Prompts("default system"),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEmpty(model.Calls);
        Assert.All(model.Calls, call => Assert.Equal("class system", call.SystemPrompt));
        Assert.Equal("An overview of the page.", document[1]?.Overview);
    }

    [Fact]
    public async Task GenerateOverviewsAsync_WithoutAClassPrompt_UsesTheWorkflowDefault()
    {
        ScriptedChatConnector model = ScriptedChatConnector.Answering("<answer>An overview of the page.</answer>");

        await AnalyzerAnswering(model).GenerateOverviewsAsync(
            TwoPagePdf(),
            "TestOrg",
            classPrompts: null,
            Prompts("default system"),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEmpty(model.Calls);
        Assert.All(model.Calls, call => Assert.Equal("default system", call.SystemPrompt));
    }

    [Fact]
    public async Task GenerateOverviewsAsync_WithNoPrompt_CallsNoModel_AndClearsEarlierOverviews()
    {
        ScriptedChatConnector model = ScriptedChatConnector.Answering("<answer>An overview of the page.</answer>");
        DocumentAnalysisService analyzer = AnalyzerAnswering(model);
        Document document = TwoPagePdf();
        document.OverviewPromptHash = "an earlier prompt";
        document[1]!.Overview = "Earlier overview";
        document[1]!.EmbeddingVector = new EmbeddingVector("test-model", [1.0, 2.0]);

        await analyzer.GenerateOverviewsAsync(
            document,
            "TestOrg",
            classPrompts: null,
            workflowDefault: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(model.Calls);
        Assert.All(document.Pages, page => Assert.Null(page.Overview));
        Assert.All(document.Pages, page => Assert.Null(page.EmbeddingVector));
        Assert.Single(analyzer.OrphanedEmbeddingVectors);
        Assert.Equal(DocumentAnalysisService.NO_OVERVIEW_PROMPT_HASH, document.OverviewPromptHash);
    }

    [Fact]
    public async Task GenerateOverviewsAsync_ASpreadsheet_UsesTheOnePageVersion()
    {
        ScriptedChatConnector model = ScriptedChatConnector.Answering("<answer>An overview of the page.</answer>");
        var spreadsheet = new Document(
            new FileMetadata("TestOrg", "model.xlsx", [0]),
            MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet);
        _ = new Page(spreadsheet, 1, 1) { Text = "Revenue | 100" };
        var classPrompts = new PageOverviewPrompts
        {
            Pages = new PageOverviewPrompt { SystemPrompt = "pages system", UserPrompt = "Page {0}" },
            SinglePage = new PageOverviewPrompt { SystemPrompt = "single page system", UserPrompt = "Sheet {0}" },
        };

        await AnalyzerAnswering(model).GenerateOverviewsAsync(
            spreadsheet,
            "TestOrg",
            classPrompts,
            workflowDefault: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("single page system", Assert.Single(model.Calls).SystemPrompt);
    }

    [Theory]
    [InlineData("<answer>\n- **Page Title/Header:** Reunion\n<answer>", "- **Page Title/Header:** Reunion")]
    [InlineData("<answer>\n- **Page Title/Header:** Reunion\n", "- **Page Title/Header:** Reunion")]
    [InlineData("Page 1: Reunion", null)]
    public async Task GenerateOverviewsAsync_ReadsTheAnswer_EvenWhenTheReplyNeverClosesItsTag(string reply, string? expectedOverview)
    {
        Document document = TwoPagePdf();

        await AnalyzerAnswering(ScriptedChatConnector.Answering(reply)).GenerateOverviewsAsync(
            document,
            "TestOrg",
            Prompts("system"),
            workflowDefault: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.All(document.Pages, page => Assert.Equal(expectedOverview, page.Overview));
    }

    private static readonly PageOverviewPrompts _standardPrompts = PageOverviewPromptDefaults.Create();

    private static PageOverviewPrompts Prompts(string systemPrompt)
    {
        return new PageOverviewPrompts { Pages = new PageOverviewPrompt { SystemPrompt = systemPrompt, UserPrompt = "Summarise page {0}." } };
    }

    private static Document TwoPagePdf()
    {
        var document = new Document(new FileMetadata("TestOrg", "Reunion.pdf", [0]), MediaType.ApplicationPdf);
        _ = new Page(document, 1, 1) { Text = DOC_1_PAGE_1_TEXT };
        _ = new Page(document, 2, 2) { Text = DOC_1_PAGE_2_TEXT };
        return document;
    }

    private DocumentAnalysisService AnalyzerAnswering(ScriptedChatConnector model)
    {
        return new DocumentAnalysisService(
            _fixture.PlatformSettings,
            TestsLogger.CreateLogger<DocumentAnalysisService>(_testOutputHelper),
            model,
            _fixture.GetTenantModelFamilyResolver(_testOutputHelper),
            new ServiceScopeFactory(),
            DocumentsFixture.GetFileStorageManager(),
            FileFormatsUnderTest.Pdf);
    }

    [Fact]
    public async Task GenerateOverviewsSlidingWindowAsync_ImageRehydrationThrows_DoesNotAbort()
    {
        IFileStorageManager throwingStorage = Substitute.For<IFileStorageManager>();
        _ = throwingStorage.RetrieveFileBytesAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns<byte[]>(_ => throw new IOException("storage unavailable"));
        ScriptedChatConnector model = ScriptedChatConnector.Answering("<answer>An overview of the page.</answer>");

        var service = new DocumentAnalysisService(
            _fixture.PlatformSettings,
            TestsLogger.CreateLogger<DocumentAnalysisService>(_testOutputHelper),
            model,
            _fixture.GetTenantModelFamilyResolver(_testOutputHelper),
            new ServiceScopeFactory(),
            throwingStorage,
            FileFormatsUnderTest.Pdf);

        var document = new Document(new FileMetadata("org", "scan.pdf", 10, StorageProvider.Database, "storage-key"), MediaType.ApplicationPdf);
        _ = new Page(document, 1, 1) { Status = PageStatus.OCRNeedsReview };

        await service.GenerateOverviewsSlidingWindowAsync(
            [.. document.Pages],
            "org",
            systemPrompt: "system",
            userPromptTemplate: "user {0}",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(document[1]?.Overview);
        Assert.Empty(model.Calls);
    }

    [Fact]
    public void ComputeOverviewPromptHash_DifferentPrompts_ProduceDifferentHashes()
    {
        // Arrange & Act
        string hash1 = DocumentAnalysisService.ComputeOverviewPromptHash("System A", "User B");
        string hash2 = DocumentAnalysisService.ComputeOverviewPromptHash("System A", "User C");
        string hash3 = DocumentAnalysisService.ComputeOverviewPromptHash("System A", "User B");

        // Assert — different prompts produce different hashes
        Assert.NotEqual(hash1, hash2);

        // Assert — same prompts produce same hash (deterministic)
        Assert.Equal(hash1, hash3);

        // Assert — handles nulls without throwing
        string hashNull = DocumentAnalysisService.ComputeOverviewPromptHash(null, null);
        Assert.NotNull(hashNull);
        Assert.NotEmpty(hashNull);

        // Assert — hash is 64 chars (SHA256 hex)
        Assert.Equal(64, hash1.Length);
    }

    private const string DOC_1_PAGE_1_TEXT =
    """
    Reunion

    The ferry was late, as it had always been, and my father was waiting on the quay as if the thirty years had not happened. He wore the same grey coat, or one so like it that I could not tell the difference, and he lifted his hand once when he saw me at the rail. He did not wave. He had never waved.

    "You found the right boat, then," he said when I stepped onto the stones.

    "There is only one boat, Dad."

    "There used to be two." He took my bag before I could stop him and turned towards the lane that climbed from the harbour to the house. "Your mother used to say that the second one was for leaving."

    I had not heard him mention her in a decade. I followed him up the lane, past the chapel with its green door, past the shop that now sold coffee to walkers, past the bench where I had carved my initials and been made to sand them out again. The island was smaller than I remembered, the way places are when you return to them taller and tired.

    The house stood where it always had, at the top of the hill with its back to the wind. Someone had painted the window frames blue. Inside, the kitchen smelled of peat smoke and the same lemon soap, and the table was set for two, with the good plates.

    "Sit," he said. "The kettle won't be long."

    I sat. On the dresser, where the wedding photograph had once stood, there was a clock that had stopped at ten past four. I asked him about it.

    "It stopped the week she went," he said. "I never saw the point of winding it."

    "You told me she went to Glasgow."

    "I did tell you that." He poured the water over the leaves and set the pot down between us. "Drink your tea."

    We talked about the weather, which was the island's way of talking about everything else. We talked about the new pier and the old minister and the price of diesel. He asked about my work, and I told him I built bridges, which was nearly true. He nodded as if bridges were a reasonable thing for a man to build. When the tea was finished he stood, rinsed both cups, and said he would show me the room.

    My room had not changed. The narrow bed, the shelf of paperbacks with their spines bleached by the window, the map of the world with a pin still pushed into Buenos Aires, where I had once decided I would live. I sat on the bed and listened to him moving about downstairs, and I tried to remember the last evening my mother had been in the house.

    I remembered shouting. I remembered that I had been sent to bed early and that I had lain awake listening to the sea and, under the sea, my parents' voices. I remembered a door, and then the ferry horn the next morning, and my father at the breakfast table saying she had gone to stay with her sister and would write. She did write, once. A postcard of the Clyde with four words on it: Be good. Love, Mum.
    """;
    private const string DOC_1_PAGE_2_TEXT =
    """
    “Her new marriage is working out then?”
    “Well, it’s not exactly new any more, is it? Yes, they’re okay I think. I’ve just been to see her actually.”
    “You went to your mother first?”
    “Well, it’s what wives are supposed to do, isn’t it? Run back to Mummy.”
    “So you and Richard…”
    “Yes. It’s over between me and Richard.”
    “After only… three years.”
    “Not even that. Two years and ten months.”
    “And you’re sure that it’s really over?”
    She nodded. The waitress came back with cutlery and the tea things on a tray and started arranging them on the table. Carol waited until she had gone.
    “Absolutely sure. I couldn’t be more sure.”
    I waited. I knew that she wanted to tell me about it and that she would in her own time.
    “Everybody thought he was so sweet and charming, didn’t they? Wouldn’t say boo to a goose. Well he wasn’t like that. That was the public face. Richard was two different people. You only ever saw the nice one.” As she spoke she unbuttoned her jacket and hung it carefully on the back of the empty seat beside her. Then she rolled up the sleeve of her white blouse.
    “My god! You’re not telling me Richard did that?”
    “That’s the only one I can let you see in public. Richard was good at leaving no visible marks.”
    I felt my heart racing. I could barely breathe. “This is insane. Richard did that to you?” She rolled her sleeve down again and retrieved her jacket. I wanted to take her in my arms but we were sitting on opposite sides of the table. I took her hand instead. “How long, sweetheart? How long has it been going on?”
    “The first time was… oh, what does it matter? Too long. I can’t believe that I let it happen. That I believed him when he said how sorry he was, how much he loved me, how he was going to change. How he needed just one more chance. I feel like I’ve been living in a cliché. You never think it can happen to you, do you?”
    My head was swimming. “The police, Carol. You’ve got to go to the police. I’ll go with you. You can’t let him get away with this…”
    """;
}

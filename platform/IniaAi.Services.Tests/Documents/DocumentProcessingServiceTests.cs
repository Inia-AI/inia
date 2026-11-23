using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Cohere;
using IniaAi.AiConnection.Common;
using IniaAi.Common.Enums;
using IniaAi.Common.Extensions;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Configuration;
using IniaAi.Configuration.LLMProviders;
using IniaAi.Core.Services.Abstraction;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Graph;
using IniaAi.Core.Workflows.Scheduling;
using IniaAi.Core.Workflows.Interfaces;
using IniaAi.Domain.DocumentAnalysis;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Documents.Enums;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.FileFormats.Pdf;
using IniaAi.FileFormats.Excel;
using IniaAi.FileFormats.Pages;
using IniaAi.FileFormats.Pdf.Pdfium;
using IniaAi.FileFormats.Word;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Services.Documents;
using IniaAi.Testing.Documents;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection;
using IniaAi.Testing.DependencyInjection.Mocks;
using IniaAi.Testing.XunitAttributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using System.Text;
using FileMetadata = IniaAi.Domain.Entities.Documents.FileMetadata;
using ServiceProvider = IniaAi.Testing.DependencyInjection.ServiceProvider;

namespace IniaAi.Services.Tests.Documents;

public class DocumentProcessingServiceTests : IClassFixture<DocumentsFixture>
{
    private const string PICTURE_TEXT = "Picture text.";
    // "An overview of the page." in words, as the word-hash stand-in counts them.
    private const int OVERVIEW_WORDS = 5;
    private static readonly TimeSpan _stallLimit = TimeSpan.FromMinutes(1);
    private readonly DocumentProcessingService _documentProcessing;
    private readonly IWorkflowHandler _workflowHandler;
    private readonly DocumentsFixture _fixture;
    private readonly IUserDataUnitOfWork userDataUnitOfWork;
    private readonly ServiceProvider _serviceProvider;
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public DocumentProcessingServiceTests(
        DocumentsFixture fixture,
        ITestOutputHelper testOutputHelper)
    {
        ServiceProvider serviceProvider = _serviceProvider = new ServiceProvider();
        IServiceScopeFactory serviceScopeFactory = new ServiceScopeFactory(serviceProvider);
        IServiceScope scope = serviceScopeFactory.CreateScope();
        userDataUnitOfWork = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();

        _fixture = fixture;
        _testOutputHelper = testOutputHelper;
        _serviceScopeFactory = serviceScopeFactory;

        _documentProcessing = CreateDocumentProcessing(ScriptedChatConnector.Answering("<answer>An overview of the page.</answer>"));

        _workflowHandler = new WorkflowHandler(
            TestsLogger.CreateLogger<WorkflowHandler>(testOutputHelper),
            fixture.PlatformSettingsOptions,
            serviceScopeFactory,
            fixture.GetWorkflowOutputService(),
            _documentProcessing,
            DocumentsFixture.GetFileStorageManager(),
            Substitute.For<IEntityPlaceholderResolver>(),
            new WorkflowGraphBuilder(),
            new LevelScheduler(
                new BlockConcurrencyCap(fixture.PlatformSettingsOptions),
                TestsLogger.CreateLogger<LevelScheduler>(testOutputHelper)),
            Substitute.For<IExecutedBlockWriter>());
    }

    [IgnoreInCiCdFact("Flaky test")]
    public async Task ProcessDocumentsAsync_MultipleFiles_ProcessesCorrectlyAsync()
    {
        // Arrange
        FileMetadata file1 = new("IniaAi", "Topics_10_Pages.pdf", _fixture.PdfSample3.BinaryData.ToArray());
        FileMetadata file2 = new("IniaAi", "WithGraphicsSearchable.pdf", _fixture.PdfSample2.BinaryData.ToArray());
        FileMetadata file3 = new("IniaAi", "ManagerQs1.png", _fixture.ImageSample1.BinaryData.ToArray());
        Document document1 = new(file1, MediaType.ApplicationPdf) { FileMetadata = file1 };
        Document document2 = new(file2, MediaType.ApplicationPdf) { FileMetadata = file2 };
        Document document3 = new(file3, MediaType.ImagePng) { FileMetadata = file3 };
        List<DocumentInfo> documentInfos = [
            new DocumentInfo(document1.Id) { Document = document1 },
            new DocumentInfo(document2.Id) { Document = document2 },
            new DocumentInfo(document3.Id) { Document = document3 },
        ];

        // Act
        await _documentProcessing.ProcessDocumentsAsync(
            documentInfos,
            userDataUnitOfWork,
            WithDefault(new Workflow(), PageOverviewPromptDefaults.Create()),
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(10, documentInfos[0].Document?.Pages.Count);
        //Assert.Contains("Page 1", documentInfos[0].Document?.Pages.FirstOrDefault()?.Text);
        Assert.False(string.IsNullOrWhiteSpace(documentInfos[0].Document?.Pages.FirstOrDefault()?.Overview));
        Assert.NotNull(documentInfos[0].Document?.Pages.FirstOrDefault()?.EmbeddingVector);
        Assert.DoesNotContain("ERROR", documentInfos[0].Document?.Pages.FirstOrDefault()?.EmbeddingVector?.Model);

        Assert.Equal(3, documentInfos[1].Document?.Pages.Count);
        Assert.Contains("funding your future with funds", documentInfos[1].Document?.Pages.FirstOrDefault()?.Text);
        Assert.False(string.IsNullOrWhiteSpace(documentInfos[1].Document?.Pages.FirstOrDefault()?.Overview));
        Assert.NotNull(documentInfos[1].Document?.Pages.FirstOrDefault()?.EmbeddingVector);
        Assert.DoesNotContain("ERROR", documentInfos[1].Document?.Pages.FirstOrDefault()?.EmbeddingVector?.Model);

        Assert.Equal(1, documentInfos[2].Document?.Pages.Count);
    }

    [Fact]
    public async Task ProcessDocumentsAsync_WordDocument_ProducesPagesAsync()
    {
        // Arrange
        byte[] word = WordFixtures.Document(
            "Monitoring Summary",
            "WORD-SENTINEL-NAV is 1,234,567 USD as of the reporting date.",
            WordFixtures.PAGE_BREAK,
            "Second page narrative about share classes.");

        FileMetadata file = new("IniaAi", "monitoring_report_no_overview.docx", word);
        Document document = new(file, MediaType.ApplicationVndOpenXmlFormatsOfficedocumentWordprocessingmlDocument)
        {
            FileMetadata = file,
        };
        List<DocumentInfo> documentInfos = [new DocumentInfo(document.Id) { Document = document }];

        // Act
        await _documentProcessing.ProcessDocumentsAsync(
            documentInfos,
            userDataUnitOfWork,
            workflow: null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        List<Page> pages = [.. document.Pages.OrderBy(page => page.PageNumber)];
        Assert.Equal(Enumerable.Range(1, pages.Count), pages.Select(page => page.PageNumber));
        string text = string.Join('\n', pages.Select(page => page.Text));
        int summary = text.IndexOf("Monitoring Summary", StringComparison.Ordinal);
        int sentinel = text.IndexOf("WORD-SENTINEL-NAV", StringComparison.Ordinal);
        int narrative = text.IndexOf("Second page narrative", StringComparison.Ordinal);
        Assert.True(summary >= 0 && sentinel > summary && narrative > sentinel, "The pages lost text or changed its order.");
        Assert.All(document.Pages, p => Assert.Equal(PageStatus.ProcessedSuccessfully, p.Status));
    }

    [Fact]
    public void PageCreatorFor_AgreesWithProducesPages_ForEveryMediaType()
    {
        foreach (MediaType mediaType in Enum.GetValues<MediaType>())
        {
            Assert.Equal(mediaType.ProducesPages(), _documentProcessing.PageCreatorFor(mediaType) is not null);
        }
    }

    [Theory]
    [InlineData("baseline_no_overview.txt", MediaType.TextPlain)]
    [InlineData("baseline_no_overview.md", MediaType.TextMarkdown)]
    public async Task ProcessDocumentsAsync_TextDocument_MakesOnePageFromTheTextWithoutAnExtractor_AndDropsTheByteOrderMarkAsync(string fileName, MediaType mediaType)
    {
        // Arrange
        const string text = "# Quarterly report\n\nTEXT-SENTINEL-NAV is 1,234,567 USD as of the reporting date.\n\n| Segment | Revenue |\n|---|---|\n| Retail | 12.5 |\n";
        byte[] bytesWithBom = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)];
        FileMetadata file = new("IniaAi", fileName, bytesWithBom);
        Document document = new(file, mediaType)
        {
            FileMetadata = file,
        };
        List<DocumentInfo> documentInfos = [new DocumentInfo(document.Id) { Document = document }];

        // Act
        await _documentProcessing.ProcessDocumentsAsync(
            documentInfos,
            userDataUnitOfWork,
            workflow: null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Page page = Assert.Single(document.Pages);
        Assert.Equal(1, page.PageNumber);
        Assert.Equal(text, page.Text);
        Assert.Equal(text, page.RawText);
        Assert.Equal(PageStatus.ProcessedSuccessfully, page.Status);
        Assert.NotNull(document.ProcessedAt);
    }

    [Fact]
    public async Task ProcessDocumentsAsync_Csv_MakesPagesOfFiftyRows_EachUnderTheHeader_AsCompactAsTheFileAsync()
    {
        string csv = "Date,Account,Amount\n" + string.Join("\n", Enumerable.Range(1, 120).Select(row => $"2026-01-{row % 28 + 1:00},Account {row},{row * 10}"));

        Document document = await ProcessAsync(csv, "ledger.csv", MediaType.TextCsv);

        Assert.Equal([1, 2, 3], document.Pages.OrderBy(page => page.PageNumber).Select(page => page.PageNumber));
        Assert.All(document.Pages, page => Assert.StartsWith("|Date|Account|Amount|\n|---|---|---|\n", page.Text));
        Assert.Equal([50, 50, 20], document.Pages.OrderBy(page => page.PageNumber).Select(page => page.Text!.Split('\n').Length - 2));
        Assert.Contains("|2026-01-02|Account 1|10|", document[1]!.Text);
        Assert.Contains("|2026-01-09|Account 120|1200|", document[3]!.Text);
        Assert.True(document.Pages.Sum(page => page.Text!.Length) < csv.Length * 1.3, "A CSV page holds its rows as a table, not a line per cell.");
        Assert.All(document.Pages, page => Assert.Equal(PageStatus.ProcessedSuccessfully, page.Status));
    }

    [Fact]
    public async Task ProcessDocumentsAsync_CsvRowWiderThanItsHeader_ReadsTheExtraFieldUnderANumberedColumnAsync()
    {
        const string csv = "Name,Amount\nAcme,100\nBeta,200,unexpected note\n";

        Document document = await ProcessAsync(csv, "wide-row.csv", MediaType.TextCsv);

        Page page = Assert.Single(document.Pages);
        Assert.StartsWith("|Name|Amount|Column 3|\n|---|---|---|\n", page.Text);
        Assert.Contains("|Acme|100| |", page.Text);
        Assert.Contains("|Beta|200|unexpected note|", page.Text);
    }

    [Fact]
    public async Task ProcessDocumentsAsync_CsvRowOfThousandsOfFields_ReadsTheColumnsAWorkbookPageReadsAsync()
    {
        string csv = "Name,Amount\n" + string.Join(',', Enumerable.Range(1, 5_000)) + "\n";

        Document document = await ProcessAsync(csv, "wide.csv", MediaType.TextCsv);

        string header = Assert.Single(document.Pages).Text!.Split('\n')[0];
        Assert.Equal(ExcelUtils.MAX_COLUMNS, header.Count(character => character == '|') - 1);
        Assert.EndsWith($"|Column {ExcelUtils.MAX_COLUMNS}|", header);
    }

    [Fact]
    public async Task ProcessDocumentsAsync_CsvWithSemicolons_ReadsItsColumns_AndKeepsAPipeInACellAsync()
    {
        const string csv = "Name;Amount\nAcme | Holdings;1.234,50\n";

        Document document = await ProcessAsync(csv, "semicolons.csv", MediaType.TextCsv);

        Page page = Assert.Single(document.Pages);
        Assert.Equal("|Name|Amount|\n|---|---|\n|Acme \\| Holdings|1.234,50|", page.Text);
    }

    [Fact]
    public async Task ProcessDocumentsAsync_LongTextFile_MakesPagesOfAtMostAPage_ClosedAtParagraphs_HoldingEveryWordInOrderAsync()
    {
        string[] paragraphs = [.. Enumerable.Range(1, 60).Select(number => $"Paragraph {number} says the revenue of segment {number} grew by {number} percent over the year, as the board reported in its letter to shareholders.")];
        string text = string.Join("\n\n", paragraphs);

        Document document = await ProcessAsync(text, "letter.txt", MediaType.TextPlain);

        Assert.True(document.Pages.Count > 1);
        Assert.All(document.Pages, page => Assert.InRange(page.Text!.Length, 1, TextPages.PAGE_CHARACTERS));
        Assert.All(document.Pages, page => Assert.EndsWith("shareholders.", page.Text));
        Assert.Equal(Words(text), document.Pages.OrderBy(page => page.PageNumber).SelectMany(page => Words(page.Text!)));
    }

    [Fact]
    public async Task ProcessDocumentsAsync_MarkdownHeading_StartsANewPage_OnceThePageIsHalfFullAsync()
    {
        string section = string.Join("\n\n", Enumerable.Range(1, 30).Select(number => $"Line {number} of the section explains one part of the credit view in plain words."));
        string markdown = $"# Overview\n\n{section}\n\n## Risks\n\n{section}";

        Document document = await ProcessAsync(markdown, "memo.md", MediaType.TextMarkdown);

        Assert.Equal(2, document.Pages.Count);
        Assert.StartsWith("# Overview", document[1]!.Text);
        Assert.StartsWith("## Risks", document[2]!.Text);
    }

    [Fact]
    public async Task ProcessDocumentsAsync_TextParagraphLongerThanAPage_SplitsBetweenWordsAsync()
    {
        string text = string.Join(' ', Enumerable.Range(1, 1_500).Select(number => $"word{number}"));

        Document document = await ProcessAsync(text, "one-paragraph.txt", MediaType.TextPlain);

        Assert.True(document.Pages.Count > 1);
        Assert.All(document.Pages, page => Assert.InRange(page.Text!.Length, 1, TextPages.PAGE_CHARACTERS));
        Assert.Equal(Words(text), document.Pages.OrderBy(page => page.PageNumber).SelectMany(page => Words(page.Text!)));
    }

    [Fact]
    public async Task ProcessDocumentsAsync_WordDocumentRepeatingOnePicture_ReadsThePictureOnceAsync()
    {
        byte[] png = _fixture.ImageSample1.BinaryData.ToArray();
        ScriptedChatConnector once = ScriptedChatConnector.Answering("<answer>Picture text.</answer>");
        ScriptedChatConnector repeated = ScriptedChatConnector.Answering("<answer>Picture text.</answer>");

        Document single = await ProcessAsync(WordFixtures.DocumentRepeatingOnePicture(png, pages: 1), "one-logo.docx", MediaType.ApplicationVndOpenXmlFormatsOfficedocumentWordprocessingmlDocument, CreateDocumentProcessing(once));
        Document three = await ProcessAsync(WordFixtures.DocumentRepeatingOnePicture(png, pages: 3), "three-logos.docx", MediaType.ApplicationVndOpenXmlFormatsOfficedocumentWordprocessingmlDocument, CreateDocumentProcessing(repeated));

        Assert.NotEmpty(once.Calls);
        Assert.Equal(once.Calls.Count, repeated.Calls.Count);
        Assert.Contains(PICTURE_TEXT, single[1]!.Text, StringComparison.Ordinal);
        Assert.Equal(3, three.Pages.Sum(page => Occurrences(page.Text, PICTURE_TEXT)));
    }

    [Fact]
    public async Task ProcessDocumentsAsync_WordPictureWhoseFirstReadFails_IsReadAgainOnTheNextPageAsync()
    {
        byte[] png = _fixture.ImageSample1.BinaryData.ToArray();
        ScriptedChatConnector readOnce = ScriptedChatConnector.Answering("<answer>Picture text.</answer>");
        int calls = 0;
        var failsOnce = new ScriptedChatConnector(_ => Interlocked.Increment(ref calls) == 1
            ? throw new HttpRequestException("The model service is unavailable.")
            : "<answer>Picture text.</answer>");

        _ = await ProcessAsync(WordFixtures.DocumentRepeatingOnePicture(png, pages: 1), "one-logo.docx", MediaType.ApplicationVndOpenXmlFormatsOfficedocumentWordprocessingmlDocument, CreateDocumentProcessing(readOnce));
        Document document = await ProcessAsync(WordFixtures.DocumentRepeatingOnePicture(png, pages: 3), "three-logos.docx", MediaType.ApplicationVndOpenXmlFormatsOfficedocumentWordprocessingmlDocument, CreateDocumentProcessing(failsOnce));

        Assert.Equal(1 + readOnce.Calls.Count, calls);
        Assert.Equal(PageStatus.OCRFailed, document[1]!.Status);
        Assert.All(document.Pages.Where(page => page.PageNumber > 1), page => Assert.Contains(PICTURE_TEXT, page.Text, StringComparison.Ordinal));
    }

    private Task<Document> ProcessAsync(string content, string fileName, MediaType mediaType)
    {
        return ProcessAsync(Encoding.UTF8.GetBytes(content), fileName, mediaType, _documentProcessing);
    }

    private async Task<Document> ProcessAsync(byte[] bytes, string fileName, MediaType mediaType, DocumentProcessingService processing)
    {
        FileMetadata file = new("IniaAi", fileName, bytes);
        Document document = new(file, mediaType) { FileMetadata = file };

        await processing.ProcessDocumentsAsync(
            [new DocumentInfo(document.Id) { Document = document }],
            userDataUnitOfWork,
            workflow: null,
            cancellationToken: TestContext.Current.CancellationToken);

        return document;
    }

    private static string[] Words(string text)
    {
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }

    private static int Occurrences(string? text, string value)
    {
        return text is null ? 0 : (text.Length - text.Replace(value, string.Empty, StringComparison.Ordinal).Length) / value.Length;
    }

    [Fact]
    public async Task ProcessDocumentsAsync_WithoutAnyOverviewPrompt_SummarisesAndEmbedsNothingAsync()
    {
        ScriptedChatConnector model = ScriptedChatConnector.Answering("<answer>An overview of the page.</answer>");
        Document document = TextDocument();

        await CreateDocumentProcessing(model).ProcessDocumentsAsync(
            [new DocumentInfo(document.Id) { Document = document }],
            userDataUnitOfWork,
            new Workflow(),
            cancellationToken: TestContext.Current.CancellationToken);

        Page page = Assert.Single(document.Pages);
        Assert.Null(page.Overview);
        Assert.Null(page.EmbeddingVector);
        Assert.Empty(model.Calls);
    }

    [Fact]
    public async Task ProcessDocumentsAsync_SummarisesAClassWithItsOwnPrompt_AndEmbedsTheOverviewAsync()
    {
        ScriptedChatConnector model = ScriptedChatConnector.Answering("<answer>An overview of the page.</answer>");
        var workflow = new Workflow();
        _ = new DocumentClass(workflow, "report") { PageOverviews = OverviewPrompts("class system") };
        Document document = TextDocument();

        await CreateDocumentProcessing(model).ProcessDocumentsAsync(
            [new DocumentInfo(document.Id) { Document = document, DocumentClass = "report" }],
            userDataUnitOfWork,
            WithDefault(workflow, OverviewPrompts("default system")),
            cancellationToken: TestContext.Current.CancellationToken);

        Page page = Assert.Single(document.Pages);
        Assert.Equal("class system", Assert.Single(model.Calls).SystemPrompt);
        Assert.Equal("An overview of the page.", page.Overview);
        Assert.NotNull(page.EmbeddingVector);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ProcessDocumentsAsync_WithoutACohereKey_SendsNothingToCohere_KeepsTheErrorVector_AndWarnsOnceAsync(string? key)
    {
        const string EMBED_PATH = "/v2/embed";
        using var cohere = new LocalTestHttpServer();
        var embeddings = new CohereEmbeddingsConnector(
            TestsLogger.CreateLogger<IEmbeddingsConnector>(_testOutputHelper),
            Options.Create(new PlatformSettings { Cohere = new CohereSettings { ApiKey = key, EmbeddingsApiUrl = $"{cohere.BaseUrl}{EMBED_PATH}" } }));
        var log = new CapturingLogger<DocumentProcessingService>();
        ScriptedChatConnector model = ScriptedChatConnector.Answering("<answer>An overview of the page.</answer>");
        var workflow = new Workflow();
        _ = new DocumentClass(workflow, "report");
        Document document = TextDocument();

        await CreateDocumentProcessing(model, embeddings, log, FileFormatsUnderTest.Pdf).ProcessDocumentsAsync(
            [new DocumentInfo(document.Id) { Document = document, DocumentClass = "report" }],
            userDataUnitOfWork,
            WithDefault(workflow, OverviewPrompts("default system")),
            cancellationToken: TestContext.Current.CancellationToken);

        Page page = Assert.Single(document.Pages);
        Assert.Equal("An overview of the page.", page.Overview);
        Assert.Equal("ERROR", page.EmbeddingVector?.Model);
        Assert.Equal(0, cohere.PathHits(EMBED_PATH));
        Assert.Single(log.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task ProcessDocumentsAsync_RecordsPageEmbeddings_UnderTheRouteAndTheModelThePriceListUsesAsync()
    {
        var tokenUsage = new TestTokenUsageService();
        ScriptedChatConnector model = ScriptedChatConnector.Answering("<answer>An overview of the page.</answer>");
        var workflow = new Workflow();
        _ = new DocumentClass(workflow, "report");
        Document document = TextDocument();

        await CreateDocumentProcessing(
            model,
            new WordHashEmbeddingsConnector(),
            TestsLogger.CreateLogger<DocumentProcessingService>(_testOutputHelper),
            FileFormatsUnderTest.Pdf,
            tokenUsage).ProcessDocumentsAsync(
            [new DocumentInfo(document.Id) { Document = document, DocumentClass = "report" }],
            userDataUnitOfWork,
            WithDefault(workflow, OverviewPrompts("default system")),
            cancellationToken: TestContext.Current.CancellationToken);

        EmbeddingUsageRecord record = Assert.Single(tokenUsage.EmbeddingRecords);
        Assert.Equal(UsageType.EmbeddingPage, record.UsageType);
        Assert.Equal(ModelProviders.COHERE_API, record.Provider);
        Assert.Equal("cohere", record.Family);
        Assert.Equal("embed-v4.0", record.ModelId);
        Assert.Equal(OVERVIEW_WORDS, record.Components.Sum(static component => component.Units));
    }

    [Fact]
    public async Task ProcessDocumentsAsync_WhenTheModelCountsNoTokens_LogsTheMarkerTheAlarmReads_AndRecordsNothingAsync()
    {
        var tokenUsage = new TestTokenUsageService();
        var log = new CapturingLogger<DocumentProcessingService>();
        ScriptedChatConnector model = ScriptedChatConnector.Answering("<answer>An overview of the page.</answer>");
        var workflow = new Workflow();
        _ = new DocumentClass(workflow, "report");
        Document document = TextDocument();

        await CreateDocumentProcessing(model, new WordHashEmbeddingsConnector(countsTokens: false), log, FileFormatsUnderTest.Pdf, tokenUsage).ProcessDocumentsAsync(
            [new DocumentInfo(document.Id) { Document = document, DocumentClass = "report" }],
            userDataUnitOfWork,
            WithDefault(workflow, OverviewPrompts("default system")),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(Assert.Single(document.Pages).EmbeddingVector);
        Assert.Single(log.Entries, entry => entry.Level == LogLevel.Warning
            && entry.Message.StartsWith(EmbeddingsAnswer.UNCOUNTED_LOG_MARKER, StringComparison.Ordinal));
        Assert.Empty(tokenUsage.EmbeddingRecords);
    }

    [Fact]
    public async Task ProcessDocumentsAsync_SummarisesAClassWithoutAPrompt_WithTheWorkflowDefaultAsync()
    {
        ScriptedChatConnector model = ScriptedChatConnector.Answering("<answer>An overview of the page.</answer>");
        var workflow = new Workflow();
        _ = new DocumentClass(workflow, "report");
        Document document = TextDocument();

        await CreateDocumentProcessing(model).ProcessDocumentsAsync(
            [new DocumentInfo(document.Id) { Document = document, DocumentClass = "report" }],
            userDataUnitOfWork,
            WithDefault(workflow, OverviewPrompts("default system")),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("default system", Assert.Single(model.Calls).SystemPrompt);
        Assert.NotNull(Assert.Single(document.Pages).EmbeddingVector);
    }

    [Fact]
    public async Task ProcessDocumentsAsync_OutsideAWorkflow_KeepsTheOverviewsAnEarlierRunWroteAsync()
    {
        ScriptedChatConnector model = ScriptedChatConnector.Answering("<answer>An overview of the page.</answer>");
        DocumentProcessingService processing = CreateDocumentProcessing(model);
        var workflow = new Workflow();
        _ = new DocumentClass(workflow, "report") { PageOverviews = OverviewPrompts("class system") };
        Document document = TextDocument();
        var dbContext = (UserDataDbContext)_serviceProvider.GetService(typeof(UserDataDbContext))!;
        _ = dbContext.Documents.Add(document);
        _ = await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        await processing.ProcessDocumentsAsync(
            [new DocumentInfo(document.Id) { Document = document, DocumentClass = "report" }],
            userDataUnitOfWork,
            workflow,
            cancellationToken: TestContext.Current.CancellationToken);
        Page page = Assert.Single(document.Pages);
        EmbeddingVector? embedding = page.EmbeddingVector;
        string? promptHash = document.OverviewPromptHash;

        await processing.ProcessDocumentsAsync(
            [new DocumentInfo(document.Id) { Document = document }],
            userDataUnitOfWork,
            workflow: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(model.Calls);
        Assert.Equal("An overview of the page.", page.Overview);
        Assert.NotNull(embedding);
        Assert.Same(embedding, page.EmbeddingVector);
        Assert.Equal(promptHash, document.OverviewPromptHash);
    }

    private static Document TextDocument()
    {
        FileMetadata file = new("IniaAi", "report.txt", Encoding.UTF8.GetBytes("Revenue grew 12% in the third quarter."));
        return new Document(file, MediaType.TextPlain) { FileMetadata = file };
    }

    private static PageOverviewPrompts OverviewPrompts(string systemPrompt)
    {
        return new PageOverviewPrompts { Pages = new PageOverviewPrompt { SystemPrompt = systemPrompt, UserPrompt = "Summarise page {0}." } };
    }

    [PaidApiFact("Calls the real OCR model")]
    public async Task ProcessDocumentsAsync_WordDocumentWithImageRegion_OcrsImageContentAsync()
    {
        // Arrange
        byte[] word = WordFixtures.DocumentWithPicture(
            "WORD-IMAGE-SENTINEL narrative before the pasted screenshot.",
            _fixture.ImageSample1.BinaryData.ToArray());

        FileMetadata file = new("IniaAi", "image_region_report_no_overview.docx", word);
        Document document = new(file, MediaType.ApplicationVndOpenXmlFormatsOfficedocumentWordprocessingmlDocument)
        {
            FileMetadata = file,
        };
        List<DocumentInfo> documentInfos = [new DocumentInfo(document.Id) { Document = document }];

        // Act
        await CreateDocumentProcessing(_fixture.GetChatConnector(_testOutputHelper)).ProcessDocumentsAsync(
            documentInfos,
            userDataUnitOfWork,
            workflow: null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Page page = Assert.Single(document.Pages);
        Assert.Equal(PageStatus.OCRSuccessful, page.Status);
        Assert.Contains("WORD-IMAGE-SENTINEL", page.Text);
        Assert.NotEqual(page.RawText, page.Text);
    }

    [IgnoreInCiCdFact("Flaky test")]
    public async Task LoadContextToWorkflowAsync_MultipleFilesAndFileTypesMultipleQueries_FillsWorkflowAsync()
    {
        // Arrange
        string? onDocProcessedIndicator = null;
        Workflow workflow = new();
        Section section = new("Default", workflow);
        Block block1 = new(BlockTypes.AiQuery, section) { Name = "Block 1", ReplacementTag = "block1", Properties = new AiQueryProperties { ShouldUsePageImages = true } };
        block1.SupportedDocumentClasses.Add("MultiPageSearchablePdf");
        _ = new RagSettings(block1) { Type = RagTypes.WholeDocument };
        Block block2 = new(BlockTypes.AiQuery, section) { Name = "Block 2", ReplacementTag = "block2", Properties = new AiQueryProperties { ShouldUsePageImages = true } };
        _ = new RagSettings(block2) { Type = RagTypes.WholeDocument };
        Block block3 = new(BlockTypes.AiQuery, section) { Name = "Block 3", ReplacementTag = "block3", Properties = new AiQueryProperties { ShouldUsePageImages = true } };
        block3.SupportedDocumentClasses.Add("MultiPageSearchablePdf");
        block3.SupportedDocumentClasses.Add("WithGraphicsSearchablePdf");
        _ = new RagSettings(block3) { Type = RagTypes.WholeDocument };
        Block block4 = new(BlockTypes.Merge, section) { Name = "Block 4", ReplacementTag = "block4" };
        FileMetadata file1 = new("IniaAi", "MultiPageSearchable.pdf", _fixture.PdfSample1.BinaryData.ToArray());
        FileMetadata file2 = new("IniaAi", "WithGraphicsSearchable.pdf", _fixture.PdfSample2.BinaryData.ToArray());
        FileMetadata file3 = new("IniaAi", "ManagerQs1.png", _fixture.ImageSample1.BinaryData.ToArray());
        Document document1 = new(file1, MediaType.ApplicationPdf) { FileMetadata = file1 };
        Document document2 = new(file2, MediaType.ApplicationPdf) { FileMetadata = file2 };
        Document document3 = new(file3, MediaType.ImagePng) { FileMetadata = file3 };
        List<DocumentInfo> documents =
        [
            new DocumentInfo(document1.Id) { Document = document1, DocumentClass = "MultiPageSearchablePdf" },
            new DocumentInfo(document2.Id) { Document = document2, DocumentClass = "WithGraphicsSearchablePdf" },
            new DocumentInfo(document3.Id) { Document = document3 },
        ];
        await _documentProcessing.ProcessDocumentsAsync(
            documents,
            userDataUnitOfWork,
            workflow: null);

        // Act
        await _documentProcessing.LoadContextToWorkflowAsync(
            documents,
            workflow,
            _workflowHandler.AllPages,
            onDocumentProcessed: async document => await Task.Run(() => onDocProcessedIndicator += "success|"));

        // Assert
        Assert.Null(onDocProcessedIndicator);

        _ = Assert.Single(block1.Documents);
        Assert.Equal("MultiPageSearchable", block1.Documents[0].Name);
        Assert.Null(block1.Documents.FirstOrDefault(d => d.Name.Contains("WithGraphicsSearchable")));
        Assert.Null(block1.Documents.FirstOrDefault(d => d.Name.Contains("ManagerQs1")));
        Assert.Equal(10, block1.Documents[0].Pages.Count);
        //Assert.Contains("Page 1", block1.Documents[0].Pages.FirstOrDefault()?.Text);

        Assert.Equal(3, block2.Documents.Count);
        Assert.Contains(block2.Documents, d => d.Name.Contains("WithGraphicsSearchable"));
        Assert.Contains(block2.Documents, d => d.Name.Contains("ManagerQs1"));
        Assert.Contains(block2.Documents, d => d.Name.Contains("MultiPageSearchable"));

        Assert.Equal(2, block3.Documents.Count);
        //Assert.Contains("Page 1", block3.Documents[0].Pages.FirstOrDefault()?.Text);
        Assert.Null(block3.Documents.FirstOrDefault(d => d.Name.Contains("ManagerQs1")));

        Assert.Empty(block4.Documents);
    }

    [Fact]
    public async Task LoadContextToWorkflowAsync_DocumentProducedByBlock_CarriesItsClassAndAttachesToScopedBlockAsync()
    {
        // Arrange
        const string SPREAD_CLASS = "Spread";
        Workflow workflow = new();
        Section section = new("Default", workflow);
        Block consumer = new(BlockTypes.AiQuery, section) { Name = "Reads the spread", ReplacementTag = "readsSpread" };
        consumer.SupportedDocumentClasses.Add(SPREAD_CLASS);
        _ = new RagSettings(consumer) { Type = RagTypes.WholeDocument };

        FileMetadata fileMetadata = new("", "json2xlsx-spread.xlsx", [1, 2, 3]);
        Document produced = new(fileMetadata, MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet);
        _ = new Page(produced, 1, filePageNumber: 1) { Text = "Revenue 100" };
        DocumentInfo producedInfo = new(produced.Id) { Document = produced, DocumentClass = SPREAD_CLASS };
        List<Page> allPages = [];

        Assert.Null(produced.Class);

        // Act
        await _documentProcessing.LoadContextToWorkflowAsync(
            [producedInfo],
            workflow,
            allPages,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SPREAD_CLASS, produced.Class);
        Assert.Same(produced, Assert.Single(consumer.Documents));

        Document? foundByClass = allPages
            .Select(page => page.Document)
            .FirstOrDefault(doc => doc is not null
                && string.Equals(doc.Class, SPREAD_CLASS, StringComparison.OrdinalIgnoreCase));

        Assert.Same(produced, foundByClass);
    }

    [Fact]
    public async Task LoadContextToWorkflowAsync_ProducedDocumentHiddenFromOutput_StillAttachesToScopedBlockAsync()
    {
        const string WORKING_CLASS = "ExtractedRows";
        Workflow workflow = new();
        Section section = new("Default", workflow);
        Block consumer = new(BlockTypes.AiQuery, section) { Name = "Reads the rows", ReplacementTag = "readsRows" };
        consumer.SupportedDocumentClasses.Add(WORKING_CLASS);
        _ = new RagSettings(consumer) { Type = RagTypes.WholeDocument };

        FileMetadata fileMetadata = new("", "xlsx-extractedRows.xlsx", [1, 2, 3]);
        Document produced = new(fileMetadata, MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet);
        _ = new Page(produced, 1, filePageNumber: 1) { Text = "Row 1 | 100" };
        DocumentInfo producedInfo = new(produced.Id)
        {
            Document = produced,
            DocumentClass = WORKING_CLASS,
            ShouldBeDisplayedInOutput = false,
        };

        await _documentProcessing.LoadContextToWorkflowAsync(
            [producedInfo],
            workflow,
            [],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Same(produced, Assert.Single(consumer.Documents));
    }

    [IgnoreInCiCdFact("Flaky test")]
    public async Task LoadContextToWorkflowAsync_TwiceToTheSameWorkflowHandler_NoDuplicationAsync()
    {
        // Arrange
        Workflow workflow = new();
        Section section = new("Default", workflow);
        Block block1 = new(BlockTypes.AiQuery, section) { Name = "Block 1", ReplacementTag = "block1", SupportedDocumentClasses = ["Any"] };
        _ = new RagSettings(block1) { Type = RagTypes.WholeDocument };
        FileMetadata file2 = new("IniaAi", "WithGraphicsSearchable.pdf", _fixture.PdfSample2.BinaryData.ToArray());
        FileMetadata file3 = new("IniaAi", "ManagerQs1.png", _fixture.ImageSample1.BinaryData.ToArray());
        Document document2 = new(file2, MediaType.ApplicationPdf) { FileMetadata = file2 };
        Document document3 = new(file3, MediaType.ImagePng) { FileMetadata = file3 };
        List<DocumentInfo> documents =
        [
            new DocumentInfo(document2.Id) { Document = document2,  DocumentClass = "WithGraphicsSearchablePdf" },
            new DocumentInfo(document3.Id) { Document = document3 },
        ];
        await _documentProcessing.ProcessDocumentsAsync(
            documents,
            userDataUnitOfWork,
            workflow: null);

        // Act
        await _documentProcessing.LoadContextToWorkflowAsync(
            documents,
            workflow,
            _workflowHandler.AllPages);

        // Assert
        Assert.Equal(2, block1.Documents.Count);
        Assert.NotNull(block1.Documents.FirstOrDefault(d => d.Name.Contains("WithGraphicsSearchable")));
        Assert.NotNull(block1.Documents.FirstOrDefault(d => d.Name.Contains("ManagerQs1")));

        // Act
        await _documentProcessing.LoadContextToWorkflowAsync(
            documents,
            workflow,
            _workflowHandler.AllPages);

        // Assert
        Assert.Equal(2, block1.Documents.Count);
        Assert.NotNull(block1.Documents.FirstOrDefault(d => d.Name.Contains("WithGraphicsSearchable")));
        Assert.NotNull(block1.Documents.FirstOrDefault(d => d.Name.Contains("ManagerQs1")));
    }

    [IgnoreInCiCdFact("Flaky test")]
    public async Task ProcessDocumentsAsync_WithOcr_ProcessesCorrectlyAsync()
    {
        // Arrange
        FileMetadata file1 = new("IniaAi", "ManagerQs1.png", _fixture.ImageSample1.BinaryData.ToArray());
        Document document = new(file1, MediaType.ImagePng) { FileMetadata = file1 };
        var documentInfo = new DocumentInfo(document.Id) { ShouldUseOcr = true, Document = document };
        List<DocumentInfo> documentInfos = [documentInfo];

        // Act
        await _documentProcessing.ProcessDocumentsAsync(
            documentInfos,
            userDataUnitOfWork,
            WithDefault(new Workflow(), PageOverviewPromptDefaults.Create()),
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        _ = Assert.Single(document.Pages);
        Assert.NotNull(document.Pages.FirstOrDefault()?.RawText);
        Assert.False(string.IsNullOrWhiteSpace(document.Pages.FirstOrDefault()?.RawText));
        Assert.False(string.IsNullOrWhiteSpace(document.Pages.FirstOrDefault()?.Overview));
        Assert.NotNull(document.Pages.FirstOrDefault()?.EmbeddingVector);
        Assert.DoesNotContain("ERROR", document.Pages.FirstOrDefault()?.EmbeddingVector?.Model);
    }

    [Fact]
    public async Task ProcessDocumentsAsync_APdfThatNeedsOcr_ReadsItWithOneWorker_SoAOneWorkerLimitDoesNotStallItAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var oneWorker = new PdfiumPdfFormat(new PdfiumWorkerLimit(1));
        DocumentProcessingService processing = CreateDocumentProcessing(ScriptedChatConnector.Answering("<answer>Scanned page text.</answer>"), oneWorker);
        FileMetadata file = new("IniaAi", "scan.pdf", PdfFixtures.Plain("Scanned page text"));
        Document document = new(file, MediaType.ApplicationPdf) { FileMetadata = file };
        List<DocumentInfo> documentInfos = [new DocumentInfo(document.Id) { ShouldUseOcr = true, Document = document }];

        await Task.Run(() => processing.ProcessDocumentsAsync(documentInfos, userDataUnitOfWork, workflow: null, cancellationToken: cancellationToken), cancellationToken)
            .WaitAsync(_stallLimit, cancellationToken);

        Page page = Assert.Single(document.Pages);
        Assert.NotEqual(PageStatus.OCRFallback, page.Status);
    }

    [Fact]
    public async Task ProcessDocumentsAsync_PreProcessedDocumentThatHasADoublePageInTheMiddle_AddsTwoPagesCorrectlyAsync()
    {
        // Arrange
        FileMetadata file1 = new("IniaAi", "double_pages.pdf", _fixture.DoublePagesPdfSample.BinaryData.ToArray());
        Document document = new(file1, MediaType.ApplicationPdf) { FileMetadata = file1 };
        _ = new Page(document, 1, 1)
        {
            FilePageNumber = 1,
            RawText = "This is page 1 text. This page is a left side of a double page.",
            Overview = "Overview of page 1.",
            Status = PageStatus.ProcessedSuccessfully,
            Location = PageLocation.LeftSide,
        };
        _ = new Page(document, 2, 3)
        {
            FilePageNumber = 1,
            RawText = "This is page 2 text. This page is a right side of a double page.",
            Overview = "Overview of page 2.",
            Status = PageStatus.ProcessedSuccessfully,
            Location = PageLocation.RightSide,
        };
        _ = new Page(document, 3, 3)
        {
            FilePageNumber = 2,
            RawText = "ERROR: Failed to extract text from this page.",
            Overview = "ERROR: Overview extraction failed.",
            Status = PageStatus.ExtractedWithErrors,
            Location = PageLocation.FullPage,
        };
        _ = new Page(document, 4, 4)
        {
            FilePageNumber = 3,
            RawText = "This is page 3 text.",
            Overview = "Overview of page 3.",
            Status = PageStatus.ProcessedSuccessfully,
            Location = PageLocation.FullPage,
        };
        var documentInfo = new DocumentInfo(document.Id) { Document = document };
        List<DocumentInfo> documentInfos = [documentInfo];

        // Act
        await _documentProcessing.ProcessDocumentsAsync(
            documentInfos,
            userDataUnitOfWork,
            workflow: null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(5, document.Pages.Count);
        Assert.Equal(1, document[1]?.FilePageNumber);
        Assert.Equal(PageLocation.LeftSide, document[1]?.Location);
        Assert.Equal("This is page 1 text. This page is a left side of a double page.", document[1]?.RawText?.Trim());
        Assert.Equal(1, document[2]?.FilePageNumber);
        Assert.Equal(PageLocation.RightSide, document[2]?.Location);
        Assert.Equal("This is page 2 text. This page is a right side of a double page.", document[2]?.RawText?.Trim());
        Assert.Equal(2, document[3]?.FilePageNumber);
        Assert.Equal(PageLocation.LeftSide, document[3]?.Location);
        Assert.StartsWith("ullamcorper", document[3]?.RawText?.Trim(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, document[4]?.FilePageNumber);
        Assert.Equal(PageLocation.RightSide, document[4]?.Location);
        Assert.StartsWith("quis", document[4]?.RawText?.Trim(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, document[5]?.FilePageNumber);
        Assert.Equal(PageLocation.FullPage, document[5]?.Location);
        Assert.Equal("This is page 3 text.", document[5]?.RawText?.Trim());
    }

    [Fact]
    public async Task ProcessDocumentsAsync_PreProcessedDocumentThatHas4DoublePageInTheMiddle_AddsFourPagesCorrectlyAsync()
    {
        // Arrange
        FileMetadata file1 = new("IniaAi", "double_pages.pdf", _fixture.DoublePagesPdfSample.BinaryData.ToArray());
        Document document = new(file1, MediaType.ApplicationPdf) { FileMetadata = file1 };
        _ = new Page(document, 1, 1)
        {
            FilePageNumber = 1,
            RawText = "ERROR: Failed to extract text from this page.",
            Overview = "ERROR: Overview extraction failed.",
            Status = PageStatus.ExtractedWithErrors,
            Location = PageLocation.FullPage,
        };
        _ = new Page(document, 2, 2)
        {
            FilePageNumber = 2,
            RawText = "ERROR: Failed to extract text from this page.",
            Overview = "ERROR: Overview extraction failed.",
            Status = PageStatus.ExtractedWithErrors,
            Location = PageLocation.FullPage,
        };
        _ = new Page(document, 3, 3)
        {
            FilePageNumber = 3,
            RawText = "This is page 3 text.",
            Overview = "Overview of page 3.",
            Status = PageStatus.ProcessedSuccessfully,
            Location = PageLocation.FullPage,
        };
        var documentInfo = new DocumentInfo(document.Id) { Document = document };
        List<DocumentInfo> documentInfos = [documentInfo];

        // Act
        await _documentProcessing.ProcessDocumentsAsync(
            documentInfos,
            userDataUnitOfWork,
            workflow: null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(5, document.Pages.Count);
        Assert.Equal(1, document[1]?.FilePageNumber);
        Assert.Equal(PageLocation.LeftSide, document[1]?.Location);
        Assert.StartsWith("Lorem", document[1]?.RawText?.Trim(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, document[2]?.FilePageNumber);
        Assert.Equal(PageLocation.RightSide, document[2]?.Location);
        Assert.StartsWith("In", document[2]?.RawText?.Trim(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, document[3]?.FilePageNumber);
        Assert.Equal(PageLocation.LeftSide, document[3]?.Location);
        Assert.StartsWith("ullamcorper", document[3]?.RawText?.Trim(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, document[4]?.FilePageNumber);
        Assert.Equal(PageLocation.RightSide, document[4]?.Location);
        Assert.StartsWith("quis", document[4]?.RawText?.Trim(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, document[5]?.FilePageNumber);
        Assert.Equal(PageLocation.FullPage, document[5]?.Location);
        Assert.Equal("This is page 3 text.", document[5]?.RawText?.Trim());
    }

    [Fact]
    public async Task ProcessDocumentsAsync_DocumentWithDoublePages_ProcessesCorrectlyAsync()
    {
        // Arrange
        FileMetadata file = new("IniaAi", "double_pages.pdf", _fixture.DoublePagesPdfSample.BinaryData.ToArray());
        Document document = new(file, MediaType.ApplicationPdf) { FileMetadata = file };
        var documentInfo = new DocumentInfo(document.Id) { Document = document };
        List<DocumentInfo> documentInfos = [documentInfo];

        // Act
        await _documentProcessing.ProcessDocumentsAsync(
            documentInfos,
            userDataUnitOfWork,
            workflow: null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(5, document.Pages.Count);

        // Page 1 - left side of file page 1
        Assert.Equal(1, document[1]?.FilePageNumber);
        Assert.Equal(PageLocation.LeftSide, document[1]?.Location);
        Assert.StartsWith("Lorem", document[1]?.RawText?.Trim(), StringComparison.OrdinalIgnoreCase);

        // Page 2 - right side of file page 1
        Assert.Equal(1, document[2]?.FilePageNumber);
        Assert.Equal(PageLocation.RightSide, document[2]?.Location);
        Assert.StartsWith("In", document[2]?.RawText?.Trim(), StringComparison.OrdinalIgnoreCase);

        // Page 3 - left side of file page 2
        Assert.Equal(2, document[3]?.FilePageNumber);
        Assert.Equal(PageLocation.LeftSide, document[3]?.Location);
        Assert.StartsWith("ullamcorper", document[3]?.RawText?.Trim(), StringComparison.OrdinalIgnoreCase);

        // Page 4 - right side of file page 2
        Assert.Equal(2, document[4]?.FilePageNumber);
        Assert.Equal(PageLocation.RightSide, document[4]?.Location);
        Assert.StartsWith("quis", document[4]?.RawText?.Trim(), StringComparison.OrdinalIgnoreCase);

        // Page 5 - single page (file page 3)
        Assert.Equal(3, document[5]?.FilePageNumber);
        Assert.Equal(PageLocation.FullPage, document[5]?.Location);
        Assert.StartsWith("Cras", document[5]?.RawText?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessDocumentsAsync_DocumentWithoutDoublePages_ProcessesCorrectlyAsync()
    {
        // Arrange
        FileMetadata file = new("IniaAi", "WithGraphicsSearchable.pdf", _fixture.PdfSample2.BinaryData.ToArray());
        Document document = new(file, MediaType.ApplicationPdf) { FileMetadata = file };
        var documentInfo = new DocumentInfo(document.Id) { Document = document };
        List<DocumentInfo> documentInfos = [documentInfo];

        // Act
        await _documentProcessing.ProcessDocumentsAsync(
            documentInfos,
            userDataUnitOfWork,
            workflow: null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, document.Pages.Count);

        // Verify all pages are FullPage location
        Assert.All(document.Pages, page => Assert.Equal(PageLocation.FullPage, page.Location));

        // Verify FilePageNumber matches PageNumber for all pages
        for (int i = 1; i <= document.Pages.Count; i++)
        {
            Assert.Equal(i, document[i]?.FilePageNumber);
            Assert.Equal(i, document[i]?.PageNumber);
        }

        // Verify all pages have text content
        Assert.All(document.Pages, page => Assert.False(string.IsNullOrWhiteSpace(page.RawText)));
    }

    [Fact]
    public async Task ProcessDocumentsAsync_LandscapeSinglePageDocument_ProcessesCorrectlyAsync()
    {
        // Arrange
        FileMetadata file = new("IniaAi", "single_pages_landscape.pdf", _fixture.LandscapeSinglePagePdfSample.BinaryData.ToArray());
        Document document = new(file, MediaType.ApplicationPdf) { FileMetadata = file };
        var documentInfo = new DocumentInfo(document.Id) { Document = document };
        List<DocumentInfo> documentInfos = [documentInfo];

        // Act
        await _documentProcessing.ProcessDocumentsAsync(
            documentInfos,
            userDataUnitOfWork,
            workflow: null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, document.Pages.Count);

        // Verify all pages are FullPage location (no false positives for double-page detection)
        Assert.All(document.Pages, page => Assert.Equal(PageLocation.FullPage, page.Location));

        // Verify FilePageNumber matches PageNumber for all pages
        for (int i = 1; i <= document.Pages.Count; i++)
        {
            Assert.Equal(i, document[i]?.FilePageNumber);
            Assert.Equal(i, document[i]?.PageNumber);
        }

        // Verify all pages have text content
        Assert.All(document.Pages, page => Assert.False(string.IsNullOrWhiteSpace(page.RawText)));
    }

    [Fact]
    public async Task ProcessDocumentsAsync_OutputDocumentClass_SkipsOverviewGenerationAsync()
    {
        // Arrange
        var workflow = new Workflow();
        var outputDocClass = new DocumentClass(workflow, "OutputReport")
        {
            IsOutputDocument = true,
            DocumentCategoryId = "some-category-id",
        };

        FileMetadata file = new("IniaAi", "WithGraphicsSearchable.pdf", _fixture.PdfSample2.BinaryData.ToArray());
        Document document = new(file, MediaType.ApplicationPdf) { FileMetadata = file };
        var documentInfo = new DocumentInfo(document.Id) { Document = document, DocumentClass = "OutputReport" };
        List<DocumentInfo> documentInfos = [documentInfo];

        // Act
        await _documentProcessing.ProcessDocumentsAsync(
            documentInfos,
            userDataUnitOfWork,
            workflow,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert — pages are extracted but overviews are skipped
        Assert.Equal(3, document.Pages.Count);
        Assert.All(document.Pages, page => Assert.True(
            string.IsNullOrWhiteSpace(page.Overview),
            $"Page {page.PageNumber} should have no overview but had: '{page.Overview}'"));
    }

    [Fact]
    public async Task ProcessDocumentsAsync_OutputDocumentClass_DoesNotAssignCategoryToDocumentAsync()
    {
        // Arrange
        var workflow = new Workflow();

        _ = new DocumentClass(workflow, "OutputReport")
        {
            IsOutputDocument = true,
            DocumentCategoryId = "should-not-be-assigned",
        };

        FileMetadata file = new("IniaAi", "WithGraphicsSearchable.pdf", _fixture.PdfSample2.BinaryData.ToArray());
        Document document = new(file, MediaType.ApplicationPdf) { FileMetadata = file };
        var documentInfo = new DocumentInfo(document.Id) { Document = document, DocumentClass = "OutputReport" };
        List<DocumentInfo> documentInfos = [documentInfo];

        // Act
        await _documentProcessing.ProcessDocumentsAsync(
            documentInfos,
            userDataUnitOfWork,
            workflow,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert — category assignment only happens in the overview branch
        Assert.Null(document.DocumentCategoryId);
        Assert.Null(document.Class);
    }

    [Fact]
    public async Task ProcessDocumentsAsync_DocumentClassWithCategoryId_AssignsCategoryAndClassToDocumentAsync()
    {
        // Arrange
        var workflow = new Workflow();

        _ = new DocumentClass(workflow, "FinancialReport")
        {
            IsOutputDocument = false,
            DocumentCategoryId = "test-category-id",
        };

        FileMetadata file = new("IniaAi", "WithGraphicsSearchable.pdf", _fixture.PdfSample2.BinaryData.ToArray());
        Document document = new(file, MediaType.ApplicationPdf) { FileMetadata = file };
        var documentInfo = new DocumentInfo(document.Id) { Document = document, DocumentClass = "FinancialReport" };
        List<DocumentInfo> documentInfos = [documentInfo];

        // Act
        await _documentProcessing.ProcessDocumentsAsync(
            documentInfos,
            userDataUnitOfWork,
            workflow,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("test-category-id", document.DocumentCategoryId);
        Assert.Equal("FinancialReport", document.Class);
    }

    [Fact]
    public async Task ProcessDocumentsAsync_DocumentWithStaleCategory_RefreshesCategoryFromCurrentClassAsync()
    {
        // Arrange — the document was first processed under a different class and
        // carries that run's category; the current workflow class must win so the
        // overview prompt lookup resolves against the class actually in use.
        var workflow = new Workflow();

        _ = new DocumentClass(workflow, "FocusReport")
        {
            IsOutputDocument = false,
            DocumentCategoryId = "current-category-id",
        };

        FileMetadata file = new("IniaAi", "WithGraphicsSearchable.pdf", _fixture.PdfSample2.BinaryData.ToArray());
        Document document = new(file, MediaType.ApplicationPdf)
        {
            FileMetadata = file,
            DocumentCategoryId = "stale-category-id",
            Class = "OldClass",
        };
        var documentInfo = new DocumentInfo(document.Id) { Document = document, DocumentClass = "FocusReport" };
        List<DocumentInfo> documentInfos = [documentInfo];

        // Act
        await _documentProcessing.ProcessDocumentsAsync(
            documentInfos,
            userDataUnitOfWork,
            workflow,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("current-category-id", document.DocumentCategoryId);
        Assert.Equal("FocusReport", document.Class);
    }

    [Fact]
    public async Task ProcessDocumentsAsync_DocumentWithPersistedCategoryAndNoClass_KeepsPersistedCategoryAsync()
    {
        // Arrange — without class context there is nothing to refresh from.
        FileMetadata file = new("IniaAi", "WithGraphicsSearchable.pdf", _fixture.PdfSample2.BinaryData.ToArray());
        Document document = new(file, MediaType.ApplicationPdf)
        {
            FileMetadata = file,
            DocumentCategoryId = "persisted-category-id",
        };
        var documentInfo = new DocumentInfo(document.Id) { Document = document };
        List<DocumentInfo> documentInfos = [documentInfo];

        // Act
        await _documentProcessing.ProcessDocumentsAsync(
            documentInfos,
            userDataUnitOfWork,
            workflow: null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("persisted-category-id", document.DocumentCategoryId);
    }

    [Fact]
    public async Task ProcessDocumentsAsync_DocumentWithMixedPages_ProcessesCorrectlyAsync()
    {
        // Arrange
        FileMetadata file = new("IniaAi", "mixed_pages.pdf", _fixture.MixedPagesPdfSample.BinaryData.ToArray());
        Document document = new(file, MediaType.ApplicationPdf) { FileMetadata = file };
        var documentInfo = new DocumentInfo(document.Id) { Document = document };
        List<DocumentInfo> documentInfos = [documentInfo];

        // Act
        await _documentProcessing.ProcessDocumentsAsync(
            documentInfos,
            userDataUnitOfWork,
            workflow: null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        // 4 physical pages: page 1 (portrait), page 2 (portrait), page 3 (landscape double), page 4 (portrait)
        // Should result in 5 logical pages
        Assert.Equal(5, document.Pages.Count);

        // Page 1 - portrait single page (file page 1)
        Assert.Equal(1, document[1]?.FilePageNumber);
        Assert.Equal(PageLocation.FullPage, document[1]?.Location);
        Assert.StartsWith("Lorem", document[1]?.RawText?.Trim(), StringComparison.OrdinalIgnoreCase);

        // Page 2 - portrait single page (file page 2)
        Assert.Equal(2, document[2]?.FilePageNumber);
        Assert.Equal(PageLocation.FullPage, document[2]?.Location);
        Assert.StartsWith("In non mauris justo", document[2]?.RawText?.Trim(), StringComparison.OrdinalIgnoreCase);

        // Page 3 - left side of landscape double page (file page 3)
        Assert.Equal(3, document[3]?.FilePageNumber);
        Assert.Equal(PageLocation.LeftSide, document[3]?.Location);
        Assert.StartsWith("ullamcorper", document[3]?.RawText?.Trim(), StringComparison.OrdinalIgnoreCase);

        // Page 4 - right side of landscape double page (file page 3)
        Assert.Equal(3, document[4]?.FilePageNumber);
        Assert.Equal(PageLocation.RightSide, document[4]?.Location);
        Assert.Contains("Maecenas mauris lectus", document[4]?.RawText, StringComparison.OrdinalIgnoreCase);

        // Page 5 - portrait single page (file page 4)
        Assert.Equal(4, document[5]?.FilePageNumber);
        Assert.Equal(PageLocation.FullPage, document[5]?.Location);
        Assert.StartsWith("Cras id eros", document[5]?.RawText?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    [IgnoreInCiCdFact]
    public async Task ProcessDocumentsAsync_WhenPromptHashChanges_DeletesOrphanedEmbeddingVectorsFromDb()
    {
        // Arrange — add document to EF change tracker so entities are persisted
        var dbContext = (UserDataDbContext)_serviceProvider.GetService(typeof(UserDataDbContext))!;
        FileMetadata file = new("IniaAi", "WithGraphicsSearchable.pdf", _fixture.PdfSample2.BinaryData.ToArray());
        Document document = new(file, MediaType.ApplicationPdf) { FileMetadata = file };
        _ = dbContext.Documents.Add(document);
        _ = await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        List<DocumentInfo> documentInfos = [new DocumentInfo(document.Id) { Document = document }];

        // Act 1 — first processing: generates pages, overviews, embeddings, stores hash
        await _documentProcessing.ProcessDocumentsAsync(
            documentInfos,
            userDataUnitOfWork,
            WithDefault(new Workflow(), PageOverviewPromptDefaults.Create()),
            cancellationToken: TestContext.Current.CancellationToken);

        int pageCount = document.Pages.Count;
        Assert.True(pageCount > 0);
        Assert.All(document.Pages, p => Assert.NotNull(p.EmbeddingVector));

        // Capture the original embedding vector IDs and verify they exist in DB
        List<string> originalVectorIds = [.. document.Pages
            .Where(p => p.EmbeddingVector is not null)
            .Select(p => p.EmbeddingVector!.Id)];
        foreach (string vectorId in originalVectorIds)
        {
            Assert.NotNull(dbContext.EmbeddingVectors.FirstOrDefault(v => v.Id == vectorId));
        }

        // Force hash mismatch so overviews + embeddings are cleared on next run
        document.OverviewPromptHash = "FORCE_MISMATCH";
        _ = await userDataUnitOfWork.CompleteAsync(TestContext.Current.CancellationToken);

        // Act 2 — second processing: hash mismatches, clears overviews + embeddings, regenerates
        await _documentProcessing.ProcessDocumentsAsync(
            documentInfos,
            userDataUnitOfWork,
            WithDefault(new Workflow(), PageOverviewPromptDefaults.Create()),
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert — original vectors are deleted from the DB
        foreach (string oldVectorId in originalVectorIds)
        {
            Assert.Null(dbContext.EmbeddingVectors.FirstOrDefault(v => v.Id == oldVectorId));
        }

        // Assert — pages still have new embeddings
        Assert.Equal(pageCount, document.Pages.Count);
        Assert.All(document.Pages, p => Assert.NotNull(p.EmbeddingVector));
    }

    [Fact]
    public void IsPageBad_GlyphRemappedText_ReturnsTrue()
    {
        string remappedText = ToGlyphRemappedText(
            """
            We have audited the accompanying statement of financial condition
            of the Company as of December 31, 2025, and the related notes to
            the statement. In our opinion, the statement presents fairly, in
            all material respects, the financial position of the Company in
            conformity with accounting principles generally accepted in the
            United States of America. Management is responsible for the
            preparation and fair presentation of the statement. Our
            responsibility is to express an opinion on the statement based
            on our audit. We conducted the audit in accordance with the
            applicable auditing standards and performed the procedures we
            considered necessary in the circumstances to obtain reasonable
            assurance about whether the statement is free of misstatement.
            """);

        Assert.True(_documentProcessing.IsPageBad(remappedText, 1));
    }

    [Theory]
    [InlineData("The Company is controlled through a board of Directors, comprising a non-executive Chairman, two\n"
        + "\u018B\u0118\u00B1\u018B\u0003\u0178\u00E5\u0173\u01B4\u00E5\u00DA\u0003\u0145\u0139\u0003\u018B\u0118\u00E5\u0003\u0018\u0145\u00B1\u0173\u00DA\u0003\u00DA\u019A\u0173\u011C\u0139\u010D\n"
        + "executive Directors and three independent non-executive Directors.")]
    [InlineData("At the reporting date, the Group has recognised \u0173\u00E5\u0178\u015E\u00E5\u00CF\u018B\u0003\u0145\u00FC\u0003\u00B1\u012C\u012C\u0003\u00FC\u019A\u012C\u012C\u0003\u0178\u00CF\u0145\u015E\u00E5")]
    [InlineData("The Group models cashflow scenarios to\n"
        + "                               \u0190\u014E\u01A4%\u00E5\u00CF\u00E5\u0135\u00C6\u00E5\u0173\u0003\u0196\u01C8\u0196\u0180\u0163          and management believes there is sufficient \n"
        + "headroom in its facilities to continue in operation.")]
    public void IsPageBad_TextLayerScrambledByAFontWithNoUnicodeMap_ReturnsTrue(string pageText)
    {
        Assert.True(_documentProcessing.IsPageBad(pageText, 54));
    }

    [Theory]
    [InlineData("Income Statement\n\nRevenue fell to £41.5m (FY24: £36.6m) and the operating loss was £2.4m.\n"
        + "Adjusted operating profit (£m)      1.2      0.6")]
    [InlineData("Spółka prowadzi działalność w Łodzi i Wrocławiu, a jej przychody wzrosły o dwanaście procent w ciągu roku.")]
    [InlineData("Công ty đã tăng doanh thu trong năm nay và mở rộng hoạt động sang các thị trường mới ở châu Á.")]
    [InlineData("Rīgā ūdeņi tīrāki")]
    [InlineData("Większa część przychodów pochodzi z umów zawartych przez spółkę zależną w Łodzi.")]
    [InlineData("Þórður Ármannsson stýrði félaginu í Reykjavík á árinu.")]
    [InlineData("Şirketin ağırlığı arttı; ışıklı tabelalar sığınağı ve çığlığı gösteriyor.")]
    [InlineData("Koryto řečiště bylo vyčištěno a zpevněno během roku.")]
    [InlineData("Zażółć gęślą jaźń, napisała spółka w raporcie rocznym.")]
    public void IsPageBad_CleanTextWithAccentedLetters_ReturnsFalse(string pageText)
    {
        Assert.False(_documentProcessing.IsPageBad(pageText, 30));
    }

    [Fact]
    public void IsPageBad_FontThatMapsOnlyItsSpaceToAControlCharacter_ReturnsFalse()
    {
        const string TEXT =
            "Sustainability report for the year under review, covering our plants and offices.\n"
            + "Energy consumption at the plant during the reporting period fell 3.7%\u0007in\u0007FY\u000722-23\u0007to\u0007the\u0007lowest level.\n"
            + "The company maintains its Quality\u0007Management\u0007System certification across all sites.\n"
            + "Training programmes for new joinees were expanded to cover safety and compliance.";

        Assert.False(_documentProcessing.IsPageBad(TEXT, 12));
    }

    [Fact]
    public void IsPageBad_NormalEnglishParagraph_ReturnsFalse()
    {
        const string TEXT =
            """
            The quarterly review covers liquidity, funding sources, and the
            valuation of positions held at the reporting date. Balances are
            reconciled against custodian statements before publication, and
            any differences are investigated and resolved by the operations
            team within five business days of the reporting date.
            """;

        Assert.False(_documentProcessing.IsPageBad(TEXT, 1));
    }

    [Fact]
    public void IsPageBad_JapaneseTextWithoutSpaces_ReturnsFalse()
    {
        const string TEXT =
            "当社は財務諸表の作成および適正な表示に関する内部統制を整備し、一般に公正妥当と認められる\n" +
            "企業会計の基準に準拠して財務諸表を作成する責任を負っています。監査人の責任は、実施した監査に\n" +
            "基づいて財務諸表に対する意見を表明することにあります。監査の過程において、経営者が採用した\n" +
            "会計方針およびその適用方法ならびに経営者によって行われた見積りの評価も含め全体としての\n" +
            "財務諸表の表示を検討しています。";

        Assert.False(_documentProcessing.IsPageBad(TEXT, 1));
    }

    [Fact]
    public void IsPageBad_SparseCoverPageWithDomainsAndPhone_ReturnsFalse()
    {
        const string TEXT = "800.555.0100 | ExampleBancorp.com | TheExampleBank.com";

        Assert.False(_documentProcessing.IsPageBad(TEXT, 1));
    }

    [Fact]
    public void IsPageBad_TableOfContentsWithDotLeaders_ReturnsFalse()
    {
        const string TEXT =
            """
            Table of Contents
            Disclaimer ................................................................................ 2
            General Information ............................................................. 4
            Contact List ................................................................................ 5
            Executive Summary ....................................................................... 7
            Market Overview ................................................................... 12
            Financial Projections .......................................................... 18
            """;

        Assert.False(_documentProcessing.IsPageBad(TEXT, 1));
    }

    [Fact]
    public void IsPageBad_TocWithDotLeadersGluedToWords_ReturnsFalse()
    {
        string leaders = new('.', 90);
        string text =
            $"""
            Table of Contents
            Japan{leaders} 3
            Canada{leaders} 7
            Germany{leaders} 12
            France{leaders} 18
            Australia{leaders} 24
            Singapore{leaders} 29
            """;

        Assert.False(_documentProcessing.IsPageBad(text, 1));
    }

    [Fact]
    public void IsPageBad_FinancialTableWithCurrencyPrefixedNumbers_ReturnsFalse()
    {
        const string TEXT =
            """
            Consolidated balance sheet
            December 31
            2024 2023
            P=98,115,973,998,761,234 P=97,121,875,071,972,551
            P=96,108,064,548,812,345 P=95,100,277,645,655,432
            P=94,115,973,998,761,234 P=93,121,875,071,972,551
            P=92,108,064,548,812,345 P=91,100,277,645,655,432
            P=90,115,973,998,761,234 P=89,121,875,071,972,551
            Total equity
            """;

        Assert.False(_documentProcessing.IsPageBad(TEXT, 1));
    }

    [Fact]
    public void IsPageBad_TocWithShortDotLeadersGluedToWords_ReturnsFalse()
    {
        string leaders = new('.', 12);
        string text =
            $"""
            Table of Contents
            Introduction{leaders} 2
            Methodology{leaders} 7
            Governance{leaders} 12
            Operations{leaders} 18
            Compliance{leaders} 24
            Appendices{leaders} 29
            """;

        Assert.False(_documentProcessing.IsPageBad(text, 1));
    }

    [Fact]
    public void IsPageBad_NumericOnlyPageWithLongTokens_ReturnsFalse()
    {
        string text = string.Join('\n', Enumerable.Repeat("1234567890123456789012", 6));

        Assert.False(_documentProcessing.IsPageBad(text, 1));
    }

    [Fact]
    public void IsPageBad_AvgWordLengthExactlyAtThreshold_ReturnsFalse()
    {
        string text = string.Join('\n', Enumerable.Repeat(new string('a', 15), 6));

        Assert.False(_documentProcessing.IsPageBad(text, 1));
    }

    [Fact]
    public void IsPageBad_LongWordsJustBelowMinimumWordCount_ReturnsFalse()
    {
        string text = string.Join('\n', Enumerable.Repeat(new string('a', 40), 4));

        Assert.False(_documentProcessing.IsPageBad(text, 1));
    }

    [Fact]
    public void IsPageBad_LongWordsAtMinimumWordCount_ReturnsTrue()
    {
        string text = string.Join('\n', Enumerable.Repeat(new string('a', 40), 5));

        Assert.True(_documentProcessing.IsPageBad(text, 1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t  ")]
    [InlineData("Page 4")]
    public void IsPageBad_BlankOrVeryShortText_ReturnsTrue(string text)
    {
        Assert.True(_documentProcessing.IsPageBad(text, 1));
    }

    [Fact]
    public void IsPageBad_ControlCharacterHeavyText_ReturnsTrue()
    {
        string text = string.Concat(Enumerable.Repeat("ab\u0001\u0002", 30));

        Assert.True(_documentProcessing.IsPageBad(text, 1));
    }

    [Fact]
    public void IsPageBad_SpacedOutSingleCharacters_ReturnsTrue()
    {
        const string TEXT = "T h e q u a r t e r l y r e v i e w c o v e r s l i q u i d i t y";

        Assert.True(_documentProcessing.IsPageBad(TEXT, 1));
    }

    private static string ToGlyphRemappedText(string plainText)
    {
        const string GLYPH_POOL =
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!\"#$%&'()*+,-./:;<=>?@[]^_`{|}~";

        Dictionary<char, char> map = new() { [' '] = 'ÿ' };
        int nextGlyph = 0;
        StringBuilder remapped = new(plainText.Length);

        foreach (char c in plainText)
        {
            if (c is '\n' or '\r')
            {
                _ = remapped.Append(c);
                continue;
            }

            if (!map.TryGetValue(c, out char mapped))
            {
                mapped = GLYPH_POOL[nextGlyph % GLYPH_POOL.Length];
                nextGlyph++;
                map[c] = mapped;
            }

            _ = remapped.Append(mapped);
        }

        return remapped.ToString();
    }

    private DocumentProcessingService CreateDocumentProcessing(IChatConnector chatConnector, IPdfFormat? pdfFormat = null)
    {
        return CreateDocumentProcessing(
            chatConnector,
            new WordHashEmbeddingsConnector(),
            TestsLogger.CreateLogger<DocumentProcessingService>(_testOutputHelper),
            pdfFormat ?? FileFormatsUnderTest.Pdf);
    }

    private DocumentProcessingService CreateDocumentProcessing(
        IChatConnector chatConnector,
        IEmbeddingsConnector embeddingsConnector,
        ILogger<DocumentProcessingService> logger,
        IPdfFormat pdfFormat,
        ITokenUsageService? tokenUsageService = null)
    {
        return new DocumentProcessingService(
            _fixture.PlatformSettings,
            logger,
            new(TestsLogger.CreateLogger<PdfExtractor>(_testOutputHelper)),
            pdfFormat,
            FileFormatsUnderTest.Workbooks,
            FileFormatsUnderTest.WordPages,
            embeddingsConnector,
            _fixture.GetDocumentAnalysisService(_testOutputHelper, chatConnector),
            _fixture.GetExcelSheetAnalysisService(_testOutputHelper, _serviceScopeFactory, chatConnector),
            _fixture.GetOcrTranscriptionService(_testOutputHelper, _serviceScopeFactory, chatConnector, pdfFormat),
            DocumentsFixture.GetFileStorageManager(),
            _fixture.GetTenantModelFamilyResolver(_testOutputHelper),
            tokenUsageService ?? Substitute.For<ITokenUsageService>());
    }

    private static Workflow WithDefault(Workflow workflow, PageOverviewPrompts prompts)
    {
        workflow.Configuration = new WorkflowConfiguration { PageOverviews = prompts };

        return workflow;
    }

}

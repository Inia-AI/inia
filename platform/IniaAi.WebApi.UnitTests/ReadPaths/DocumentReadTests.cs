using ErrorOr;
using IniaAi.Application.Commands.Documents.CreateTextDocument;
using IniaAi.Application.Commands.Documents.ProcessDocument;
using IniaAi.Application.Commands.Documents.UpdateDocument;
using IniaAi.Application.Commands.Files.UploadFile;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Documents;
using IniaAi.Application.Queries.Documents.GetDocument;
using IniaAi.Application.Queries.Documents.GetDocumentPage;
using IniaAi.Application.Queues;
using IniaAi.Common.Enums;
using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Enums;
using IniaAi.FileFormats;
using IniaAi.Services.Documents;
using IniaAi.Testing.DependencyInjection;
using IniaAi.Testing.Documents;
using IniaAi.WebApi.Contracts.Dto;
using IniaAi.WebApi.Contracts.Responses.Documents;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TextDocumentFormat = IniaAi.WebApi.Contracts.Enums.TextDocumentFormat;

namespace IniaAi.WebApi.UnitTests.ReadPaths;

public class DocumentReadTests : ReadPathTests
{
    private const string ORGANIZATION_ID = "org-read";
    private const string USER_ID = "user-read";
    private const string DOCUMENT_ID = "document-read";
    private const string FILE_METADATA_ID = "file-read";

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        _ = services.AddWorkQueue<DocumentProcessingQueue, DocumentProcessingRequest, DocumentProcessingRunner>();
        _ = services.AddTestFileStorage();
        _ = services.AddSingleton<IIntakeRefusalRecorder, LoggingIntakeRefusalRecorder>();
        _ = services.AddSingleton(Options.Create(new PlatformSettings { IniaAi = new IniaAiSettings { MaxFileSizeMb = 10 } }));
        _ = services.AddFileFormatsUnderTest();
        _ = services.AddSingleton<DocumentIntakeGate>();
    }

    [Fact]
    public void EveryMediaType_AnswersWithItsOwnName()
    {
        Dictionary<string, string> answers = Enum.GetValues<MediaType>().ToDictionary(
            mediaType => mediaType.ToString(),
            mediaType => FileDtoMapper.ToDto(new Document(new FileMetadata(ORGANIZATION_ID, "file.bin", 1, StorageProvider.Database), mediaType)).MediaType.ToString());

        AssertMatchesFixture(answers, "FileMediaTypes");
    }

    [Fact]
    public async Task AnUpload_AnswersWithTheSameJson()
    {
        byte[] pdf = PdfFixtures.Plain("Upload read 12345");
        IFormFile file = FormFileTestHelper.CreateFromBytes(pdf, "Annual report.pdf", "application/pdf");

        ErrorOr<FileDto> result = await Handler<UploadFileCommandHandler>().Handle(
            new UploadFileCommand(file, ORGANIZATION_ID, USER_ID),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError, string.Join("; ", result.ErrorsOrEmptyList.Select(error => error.Description)));
        Document stored = Assert.Single(await UserData.Documents.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(stored.Id, result.Value.Id);
        result.Value.Id = DOCUMENT_ID;
        AssertMatchesFixture(result.Value, "FileUploaded");
    }

    [Fact]
    public async Task ADocument_ReadsAsTheSameJson()
    {
        await SeedDocumentAsync(document => document.RecordProcessed(Created.AddMinutes(5)));

        ErrorOr<DocumentDetailDto> result = await Handler<GetDocumentQueryHandler>().Handle(
            new GetDocumentQuery(ORGANIZATION_ID, USER_ID, DOCUMENT_ID),
            TestContext.Current.CancellationToken);

        AssertMatchesFixture(result.Value, "Document");
    }

    [Fact]
    public async Task AnUpdatedDocument_AnswersWithTheSameJson()
    {
        await SeedDocumentAsync(document => document.RecordProcessingFailure("The file could not be read."));

        ErrorOr<DocumentDetailDto> result = await Handler<UpdateDocumentCommandHandler>().Handle(
            new UpdateDocumentCommand(
                OrganizationId: ORGANIZATION_ID,
                UserId: USER_ID,
                DocumentId: DOCUMENT_ID,
                Name: "Annual report (restated)",
                Description: "Restated figures.",
                DocumentCategoryId: "category-restated",
                WorkflowTypeId: null,
                Class: null,
                Topics: "Restatement",
                TableOfContents: null,
                IsStatic: false),
            TestContext.Current.CancellationToken);

        Assert.InRange(result.Value.UpdatedAt!.Value, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow);
        result.Value.UpdatedAt = Created;
        AssertMatchesFixture(result.Value, "DocumentUpdated");
    }

    [Fact]
    public async Task AProcessingRequest_AnswersWithTheSameJson()
    {
        await SeedDocumentAsync(document => document.RecordProcessed(Created.AddMinutes(5)));

        ErrorOr<DocumentSummaryDto> result = await Handler<ProcessDocumentCommandHandler>().Handle(
            new ProcessDocumentCommand(ORGANIZATION_ID, USER_ID, DOCUMENT_ID),
            TestContext.Current.CancellationToken);

        Assert.InRange(result.Value.ProcessingRequestedAt!.Value, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow);
        result.Value.ProcessingRequestedAt = Created.AddMinutes(10);
        AssertMatchesFixture(result.Value, "DocumentProcessingRequested");
    }

    [Fact]
    public async Task APastedTextDocument_AnswersWithTheSameJson()
    {
        ErrorOr<DocumentSummaryDto> result = await Handler<CreateTextDocumentCommandHandler>().Handle(
            new CreateTextDocumentCommand(ORGANIZATION_ID, USER_ID, "Q3 notes", "# Q3\n\nRevenue rose.", TextDocumentFormat.Markdown),
            TestContext.Current.CancellationToken);

        Assert.InRange(result.Value.CreatedAt, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow);
        Assert.InRange(result.Value.ProcessingRequestedAt!.Value, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow);
        result.Value.Id = DOCUMENT_ID;
        result.Value.CreatedAt = Created;
        result.Value.UpdatedAt = Created;
        result.Value.ProcessingRequestedAt = Created;
        AssertMatchesFixture(result.Value, "DocumentPastedText");
    }

    [Fact]
    public async Task ThePagesOfADocument_ReadAsTheSameJson()
    {
        await SeedDocumentAsync();

        PageDetailDto[] pages =
        [
            await ReadPageAsync(1),
            await ReadPageAsync(2),
            await ReadPageAsync(3),
        ];

        AssertMatchesFixture(pages, "DocumentPages");
    }

    private async Task<PageDetailDto> ReadPageAsync(int pageNumber)
    {
        ErrorOr<PageDetailDto> result = await Handler<GetDocumentPageQueryHandler>().Handle(
            new GetDocumentPageQuery(ORGANIZATION_ID, USER_ID, DOCUMENT_ID, pageNumber),
            TestContext.Current.CancellationToken);
        return result.Value;
    }

    private async Task SeedDocumentAsync(Action<Document>? configure = null)
    {
        var fileMetadata = new FileMetadata(ORGANIZATION_ID, "Annual report.pdf", 482_113, StorageProvider.Database, "stored/annual-report.pdf")
        {
            Id = FILE_METADATA_ID,
            CreatedAt = Created,
            UpdatedAt = Created,
        };
        var document = new Document(fileMetadata, MediaType.ApplicationPdf)
        {
            Id = DOCUMENT_ID,
            OrganizationId = ORGANIZATION_ID,
            Name = "Annual report",
            Description = "The audited annual report.",
            Sources = ["User Upload", "Email"],
            Topics = "Revenue, Leverage",
            TableOfContents = "1. Overview\n2. Financial statements",
            UserId = USER_ID,
            DocumentCategoryId = "category-annual",
            WorkflowTypeId = "AssetManagerCreditAnalysis",
            Class = "Annual Report",
            WasPasswordProtected = true,
            CreatedAt = Created,
            UpdatedAt = Created.AddMinutes(1),
        };
        configure?.Invoke(document);

        _ = WithId(new Page(document, 1, 1)
        {
            Status = PageStatus.ProcessedSuccessfully,
            Text = new string('A', 150) + " " + new string('B', 150),
            RawText = "raw text of page one",
            Overview = "Page one covers revenue.",
            SectionTitles = "Overview, Revenue",
            SourceUrl = "https://example.com/report#page=1",
            EmbeddingVector = new EmbeddingVector("titan-embed-v2", [0.1, 0.2, 0.3, 0.4]),
        }, "page-1");
        _ = WithId(new Page(document, 2, 3)
        {
            Status = PageStatus.OCRNeedsReview,
            Overview = "A chart of leverage by quarter.",
            SectionTitles = "   ",
        }, "page-2");
        _ = WithId(new Page(document, 3, 4)
        {
            Status = PageStatus.OCRSuccessful,
            RawText = "Short raw text.",
        }, "page-3");

        _ = UserData.Documents.Add(document);
        _ = await UserData.SaveChangesAsync(TestContext.Current.CancellationToken);
        UserData.ChangeTracker.Clear();
    }
}

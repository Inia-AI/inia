using ErrorOr;
using IniaAi.Application.Commands.Documents.CreateTextDocument;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.UploadLimits;
using IniaAi.Application.Documents;
using IniaAi.Application.UnitTests.Documents;
using IniaAi.Common.Enums;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Enums;
using IniaAi.Testing.Documents;
using IniaAi.WebApi.Contracts.Responses.Documents;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Text;
using ContractState = IniaAi.WebApi.Contracts.Enums.DocumentProcessingState;
using TextDocumentFormat = IniaAi.WebApi.Contracts.Enums.TextDocumentFormat;

namespace IniaAi.Application.UnitTests.Commands.Documents.CreateTextDocument;

public class CreateTextDocumentCommandHandlerTests : DocumentTestHost
{
    private const string TEXT = "# Quarterly report\n\nRevenue was 1,234,567 USD.";

    private readonly IFileStorageManager _storage = Substitute.For<IFileStorageManager>();
    private readonly List<(string Key, byte[] Bytes, string ContentType)> _stored = [];

    public CreateTextDocumentCommandHandlerTests()
    {
        _ = _storage.GetProviderForTenant(Arg.Any<string>()).Returns(StorageProvider.Database);
        _ = _storage.StoreFileAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>())
            .Returns(call =>
            {
                using MemoryStream copy = new();
                call.ArgAt<Stream>(4).CopyTo(copy);
                _stored.Add((call.ArgAt<string>(3), copy.ToArray(), call.ArgAt<string>(5)));
                return Task.FromResult($"stored/{call.ArgAt<string>(3)}");
            });
    }

    private CreateTextDocumentCommandHandler Handler()
    {
        return new CreateTextDocumentCommandHandler(NullLogger<CreateTextDocumentCommandHandler>.Instance, _userData, _storage, _queue, new NoUploadLimits(), FileFormatsUnderTest.PageCounter);
    }

    [Theory]
    [InlineData(TextDocumentFormat.Markdown, "Q3 notes", "Q3 notes.md", MediaType.TextMarkdown, "text/markdown")]
    [InlineData(TextDocumentFormat.PlainText, "Q3 notes", "Q3 notes.txt", MediaType.TextPlain, "text/plain")]
    [InlineData(TextDocumentFormat.Markdown, "already.MD", "already.MD", MediaType.TextMarkdown, "text/markdown")]
    public async Task Handle_StoresTheTextAsAFileOfTheFormat_AndQueuesProcessing(TextDocumentFormat format, string name, string expectedFileName, MediaType expectedMediaType, string expectedContentType)
    {
        ErrorOr<DocumentSummaryDto> result = await Handler().Handle(new CreateTextDocumentCommand(ORG_ID, USER_ID, name, TEXT, format), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(ContractState.Queued, result.Value.ProcessingState);
        Assert.Equal(expectedMediaType.ToString(), result.Value.MediaType);

        Document stored = _context.Documents.Single(d => d.Id == result.Value.Id);
        FileMetadata file = _context.FileMetadata.Single(f => f.Id == stored.FileMetadataId);
        Assert.Equal(expectedMediaType, stored.MediaType);
        Assert.Equal(Path.GetFileNameWithoutExtension(expectedFileName), file.Name);
        Assert.Equal(Path.GetExtension(expectedFileName), file.Extension);
        Assert.Equal(Encoding.UTF8.GetByteCount(TEXT), file.SizeBytes);
        Assert.Equal($"stored/{file.Id}", file.ExternalStoragePath);
        Assert.Equal(["Pasted text"], stored.Sources);
        Assert.Equal(DocumentProcessingState.Queued, stored.ProcessingState);

        (string key, byte[] bytes, string contentType) = Assert.Single(_stored);
        Assert.Equal(file.Id, key);
        Assert.Equal(TEXT, Encoding.UTF8.GetString(bytes));
        Assert.Equal(expectedContentType, contentType);
        Assert.Equal(new DocumentProcessingRequest(ORG_ID, stored.Id, USER_ID), Assert.Single(_queue.DrainPending()));
    }

    [Fact]
    public async Task Handle_StoresThePagesOfThePastedText()
    {
        string text = string.Join("\n\n", Enumerable.Repeat(new string('t', 2_100), 3));

        ErrorOr<DocumentSummaryDto> result = await Handler().Handle(new CreateTextDocumentCommand(ORG_ID, USER_ID, "notes", text, TextDocumentFormat.PlainText), TestContext.Current.CancellationToken);

        Assert.Equal(3, _context.Documents.Single(d => d.Id == result.Value.Id).CountedPages);
    }

    [Fact]
    public async Task Handle_TwoPastesWithTheSameNameAndLength_AreTwoDocuments()
    {
        ErrorOr<DocumentSummaryDto> first = await Handler().Handle(new CreateTextDocumentCommand(ORG_ID, USER_ID, "notes", "aaaa", TextDocumentFormat.PlainText), TestContext.Current.CancellationToken);
        ErrorOr<DocumentSummaryDto> second = await Handler().Handle(new CreateTextDocumentCommand(ORG_ID, USER_ID, "notes", "bbbb", TextDocumentFormat.PlainText), TestContext.Current.CancellationToken);

        Assert.NotEqual(first.Value.Id, second.Value.Id);
        Assert.Equal(2, _stored.Count);
    }

    [Theory]
    [InlineData("", "text", false)]
    [InlineData("name", "", false)]
    [InlineData("name", "text", true)]
    public void Validator_RequiresNameAndText(string name, string text, bool valid)
    {
        FluentValidation.Results.ValidationResult result = new CreateTextDocumentCommandValidator()
            .Validate(new CreateTextDocumentCommand(ORG_ID, USER_ID, name, text, TextDocumentFormat.PlainText));

        Assert.Equal(valid, result.IsValid);
    }

    [Fact]
    public void Validator_CapsTheTextAtTheBaselineLimit()
    {
        FluentValidation.Results.ValidationResult result = new CreateTextDocumentCommandValidator()
            .Validate(new CreateTextDocumentCommand(ORG_ID, USER_ID, "name", new string('x', Document.MAX_TEXT_LENGTH + 1), TextDocumentFormat.PlainText));

        Assert.False(result.IsValid);
    }
}

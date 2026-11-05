using ErrorOr;
using IniaAi.Application.Commands.Documents.ProcessDocument;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.UnitTests.Documents;
using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Enums;
using IniaAi.Domain.Specifications.Documents;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ContractState = IniaAi.WebApi.Contracts.Enums.DocumentProcessingState;

namespace IniaAi.Application.UnitTests.Commands.Documents.ProcessDocument;

public class ProcessDocumentCommandHandlerTests : DocumentTestHost
{
    private ProcessDocumentCommandHandler Handler()
    {
        return new ProcessDocumentCommandHandler(NullLogger<ProcessDocumentCommandHandler>.Instance, _userData, _queue);
    }

    [Fact]
    public async Task Handle_StampsTheRequestCommitsAndQueuesIt()
    {
        Document document = await SeedDocumentAsync(ORG_ID, "report.pdf");

        ErrorOr<DocumentSummaryDto> result = await Handler().Handle(new ProcessDocumentCommand(ORG_ID, USER_ID, document.Id), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(ContractState.Queued, result.Value.ProcessingState);
        Assert.NotNull(result.Value.ProcessingRequestedAt);
        Document stored = Reload(document.Id);
        Assert.Equal(DocumentProcessingState.Queued, stored.ProcessingState);
        DocumentProcessingRequest request = Assert.Single(_queue.DrainPending());
        Assert.Equal(new DocumentProcessingRequest(ORG_ID, document.Id, USER_ID), request);
    }

    [Fact]
    public async Task Handle_ASecondRequestWhileQueued_IsAConflict_AndAFailedDocumentCanBeAskedAgain()
    {
        Document document = await SeedDocumentAsync(ORG_ID, "report.pdf");
        _ = await Handler().Handle(new ProcessDocumentCommand(ORG_ID, USER_ID, document.Id), TestContext.Current.CancellationToken);

        ErrorOr<DocumentSummaryDto> again = await Handler().Handle(new ProcessDocumentCommand(ORG_ID, USER_ID, document.Id), TestContext.Current.CancellationToken);
        Assert.Equal(ErrorType.Conflict, again.FirstError.Type);
        Assert.Equal("Document.ProcessingAlreadyQueued", again.FirstError.Code);

        Document failed = _context.Documents.Single(d => d.Id == document.Id);
        failed.RecordProcessingFailure("The file could not be read.");
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        ErrorOr<DocumentSummaryDto> retried = await Handler().Handle(new ProcessDocumentCommand(ORG_ID, USER_ID, document.Id), TestContext.Current.CancellationToken);
        Assert.False(retried.IsError);
        Assert.Equal(ContractState.Queued, retried.Value.ProcessingState);
        Assert.Null(retried.Value.ProcessingError);
        Assert.Equal(2, _queue.DrainPending().Count);
    }

    [Fact]
    public async Task Handle_TwoRequestsRacingOnTheSameDocument_OneWins_TheOtherIsAConflict()
    {
        Document document = await SeedDocumentAsync(ORG_ID, "report.pdf");
        using IServiceScope other = _serviceProvider.CreateScope();
        IUserDataUnitOfWork otherUserData = other.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
        Document loadedByTheLoser = (await otherUserData.Documents.GetFirstOrDefaultAsync(new DocumentByIdTrackingSpec(document.Id), TestContext.Current.CancellationToken))!;

        ErrorOr<DocumentSummaryDto> winner = await Handler().Handle(new ProcessDocumentCommand(ORG_ID, USER_ID, document.Id), TestContext.Current.CancellationToken);
        loadedByTheLoser.RequestProcessing(DateTime.UtcNow);

        Assert.False(winner.IsError);
        _ = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => otherUserData.CompleteAsync(TestContext.Current.CancellationToken));
        Assert.Single(_queue.DrainPending());
    }

    [Theory]
    [InlineData("", "doc-1", false)]
    [InlineData("org", "", false)]
    [InlineData("org", "doc-1", true)]
    public void Validator_RequiresOrganizationUserAndDocument(string organizationId, string documentId, bool valid)
    {
        FluentValidation.Results.ValidationResult result = new ProcessDocumentCommandValidator()
            .Validate(new ProcessDocumentCommand(organizationId, USER_ID, documentId));

        Assert.Equal(valid, result.IsValid);
    }

    [Theory]
    [InlineData(MediaType.MessageRfc822)]
    [InlineData(MediaType.ApplicationVndMsOutlook)]
    [InlineData(MediaType.ApplicationMsWord)]
    public async Task Handle_RefusesFormatsThatNeverProducePages(MediaType mediaType)
    {
        Document document = await SeedDocumentAsync(ORG_ID, "mail.bin", mediaType);

        ErrorOr<DocumentSummaryDto> result = await Handler().Handle(new ProcessDocumentCommand(ORG_ID, USER_ID, document.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Equal("Document.NotProcessable", result.FirstError.Code);
        Assert.Empty(_queue.DrainPending());
        Assert.Equal(DocumentProcessingState.NotRequested, Reload(document.Id).ProcessingState);
    }

    [Fact]
    public async Task Handle_AnotherOrganizationsDocument_AnswersAsAnUnknownOne()
    {
        Document foreign = await SeedDocumentAsync(OTHER_ORG_ID, "theirs.pdf");

        ErrorOr<DocumentSummaryDto> missing = await Handler().Handle(new ProcessDocumentCommand(ORG_ID, USER_ID, "missing"), TestContext.Current.CancellationToken);
        ErrorOr<DocumentSummaryDto> foreignResult = await Handler().Handle(new ProcessDocumentCommand(ORG_ID, USER_ID, foreign.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ErrorType.NotFound, missing.FirstError.Type);
        Assert.Equal(ErrorType.NotFound, foreignResult.FirstError.Type);
        Assert.Empty(_queue.DrainPending());
    }
}

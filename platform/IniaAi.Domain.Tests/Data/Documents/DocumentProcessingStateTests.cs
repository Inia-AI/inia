using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Enums;

namespace IniaAi.Domain.Tests.Data.Documents;

public class DocumentProcessingStateTests
{
    private static readonly DateTime _t0 = new(2026, 8, 18, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NeverRequestedAndNeverProcessed_IsNotRequested()
    {
        Assert.Equal(DocumentProcessingState.NotRequested, new Document().ProcessingState);
    }

    [Fact]
    public void ProcessedByAnExecutionWithoutARequest_IsProcessed()
    {
        Document document = new();
        document.RecordProcessed(_t0);

        Assert.Equal(DocumentProcessingState.Processed, document.ProcessingState);
    }

    [Fact]
    public void RequestedAndNotYetProcessed_IsQueued()
    {
        Document document = new();
        document.RequestProcessing(_t0);

        Assert.Equal(DocumentProcessingState.Queued, document.ProcessingState);
    }

    [Fact]
    public void RequestedAgainAfterAnEarlierProcessing_IsQueuedUntilTheNewerProcessingLands()
    {
        Document document = new();
        document.RecordProcessed(_t0);
        document.RequestProcessing(_t0.AddMinutes(5));

        Assert.Equal(DocumentProcessingState.Queued, document.ProcessingState);

        document.RecordProcessed(_t0.AddMinutes(6));

        Assert.Equal(DocumentProcessingState.Processed, document.ProcessingState);
    }

    [Fact]
    public void AFailureReadsAsFailedUntilTheNextRequestClearsIt()
    {
        Document document = new();
        document.RequestProcessing(_t0);
        document.RecordProcessingFailure("The file could not be read.");

        Assert.Equal(DocumentProcessingState.Failed, document.ProcessingState);

        document.RequestProcessing(_t0.AddMinutes(1));

        Assert.Null(document.ProcessingError);
        Assert.Equal(_t0.AddMinutes(1), document.ProcessingRequestedAt);
        Assert.Equal(DocumentProcessingState.Queued, document.ProcessingState);
    }

    [Fact]
    public void AFailedDocumentProcessedLaterByAnExecution_ReadsProcessed_TheStaleErrorIsGone()
    {
        Document document = new();
        document.RequestProcessing(_t0);
        document.RecordProcessingFailure("The file could not be read.");

        document.RecordProcessed(_t0.AddHours(1));

        Assert.Null(document.ProcessingError);
        Assert.Equal(DocumentProcessingState.Processed, document.ProcessingState);
    }

    [Fact]
    public void RecordProcessingFailure_TruncatesToTheColumnLength()
    {
        Document document = new();

        document.RecordProcessingFailure(new string('x', Document.MAX_PROCESSING_ERROR_LENGTH + 50));

        Assert.Equal(Document.MAX_PROCESSING_ERROR_LENGTH, document.ProcessingError!.Length);
        Assert.Equal(DocumentProcessingState.Failed, document.ProcessingState);
    }
}

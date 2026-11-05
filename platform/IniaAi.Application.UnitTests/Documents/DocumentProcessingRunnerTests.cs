using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Documents;
using IniaAi.Common.Interfaces;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Enums;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Application.UnitTests.Documents;

public class DocumentProcessingRunnerTests : DocumentTestHost
{
    private readonly RecordingProcessing _processing = new();

    private DocumentProcessingRunner Runner()
    {
        return new DocumentProcessingRunner(NullLogger<DocumentProcessingRunner>.Instance, _userData, _processing);
    }

    private async Task<Document> SeedQueuedAsync(string organizationId = ORG_ID)
    {
        return await SeedDocumentAsync(organizationId, "report.pdf", configure: d => d.RequestProcessing(DateTime.UtcNow));
    }

    [Fact]
    public async Task RunAsync_HandsTheQueuedDocumentToThePipelineWithNoWorkflowAndTheRequestingUser()
    {
        Document document = await SeedQueuedAsync();

        await Runner().RunAsync(new DocumentProcessingRequest(ORG_ID, document.Id, USER_ID), TestContext.Current.CancellationToken);

        RecordedCall call = Assert.Single(_processing.Calls);
        DocumentInfo info = Assert.Single(call.DocumentInfos);
        Assert.Equal(document.Id, info.DocumentId);
        Assert.Equal(document.Id, info.Document!.Id);
        Assert.NotNull(info.Document.FileMetadata);
        Assert.Null(call.Workflow);
        Assert.Null(call.WorkflowTypeId);
        Assert.Null(call.WorkflowExecutionId);
        Assert.Equal(USER_ID, call.UserId);
        Assert.Equal(DocumentProcessingState.Processed, Reload(document.Id).ProcessingState);
        Assert.Null(Reload(document.Id).ProcessingError);
    }

    [Fact]
    public async Task RunAsync_APipelineThatReturnsWithoutMarkingTheDocumentProcessed_IsRecordedAsAFailure()
    {
        Document document = await SeedQueuedAsync();
        _processing.StampProcessed = false;

        await Runner().RunAsync(new DocumentProcessingRequest(ORG_ID, document.Id, USER_ID), TestContext.Current.CancellationToken);

        Document stored = Reload(document.Id);
        Assert.Equal(DocumentProcessingState.Failed, stored.ProcessingState);
        Assert.Contains("without marking this document processed", stored.ProcessingError);
    }

    [Fact]
    public async Task RunAsync_APipelineFailure_IsRecordedOnTheDocument()
    {
        Document document = await SeedQueuedAsync();
        _processing.Fail = new InvalidOperationException("Document media type is unknown.");

        await Runner().RunAsync(new DocumentProcessingRequest(ORG_ID, document.Id, USER_ID), TestContext.Current.CancellationToken);

        Document stored = Reload(document.Id);
        Assert.Equal(DocumentProcessingState.Failed, stored.ProcessingState);
        Assert.Equal("Document media type is unknown.", stored.ProcessingError);
    }

    [Fact]
    public async Task RunAsync_ACancelledAttempt_IsRecordedAsInterrupted_AndRethrown()
    {
        Document document = await SeedQueuedAsync();
        using CancellationTokenSource stopping = new();
        _processing.Fail = new OperationCanceledException(stopping.Token);
        _processing.OnCall = stopping.Cancel;

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Runner().RunAsync(new DocumentProcessingRequest(ORG_ID, document.Id, USER_ID), stopping.Token));

        Document stored = Reload(document.Id);
        Assert.Equal(DocumentProcessingState.Failed, stored.ProcessingState);
        Assert.Contains("interrupted", stored.ProcessingError);
    }

    [Fact]
    public async Task RunAsync_ARequestForADocumentNoLongerQueued_OrOfAnotherOrganization_IsANoOp()
    {
        Document processed = await SeedDocumentAsync(ORG_ID, "done.pdf", configure: d => d.RecordProcessed(DateTime.UtcNow));
        Document foreign = await SeedQueuedAsync(OTHER_ORG_ID);

        await Runner().RunAsync(new DocumentProcessingRequest(ORG_ID, processed.Id, USER_ID), TestContext.Current.CancellationToken);
        await Runner().RunAsync(new DocumentProcessingRequest(ORG_ID, foreign.Id, USER_ID), TestContext.Current.CancellationToken);
        await Runner().RunAsync(new DocumentProcessingRequest(ORG_ID, "missing", USER_ID), TestContext.Current.CancellationToken);

        Assert.Empty(_processing.Calls);
        Assert.Equal(DocumentProcessingState.Queued, Reload(foreign.Id).ProcessingState);
    }

    [Fact]
    public async Task RecordNotStartedAsync_LeavesTheReasonOnTheDocument()
    {
        Document document = await SeedQueuedAsync();

        await Runner().RecordNotStartedAsync(new DocumentProcessingRequest(ORG_ID, document.Id, USER_ID));

        Document stored = Reload(document.Id);
        Assert.Equal(DocumentProcessingState.Failed, stored.ProcessingState);
        Assert.Contains("not started", stored.ProcessingError);
    }

    private sealed record RecordedCall(
        List<DocumentInfo> DocumentInfos,
        Workflow? Workflow,
        string? WorkflowTypeId,
        string? WorkflowExecutionId,
        string? UserId);

    private sealed class RecordingProcessing : IDocumentProcessingService
    {
        public List<RecordedCall> Calls { get; } = [];

        public Exception? Fail { get; set; }

        public Action? OnCall { get; set; }

        public bool StampProcessed { get; set; } = true;

        public Task ProcessDocumentsAsync(
            List<DocumentInfo> documentInfos,
            IUserDataUnitOfWork userDataUnitOfWork,
            Workflow? workflow,
            string? workflowTypeId = null,
            string? workflowExecutionId = null,
            string? userId = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new RecordedCall(documentInfos, workflow, workflowTypeId, workflowExecutionId, userId));
            OnCall?.Invoke();
            if (Fail is not null)
            {
                throw Fail;
            }

            if (StampProcessed)
            {
                foreach (DocumentInfo info in documentInfos)
                {
                    info.Document!.RecordProcessed(DateTime.UtcNow);
                }
            }

            return userDataUnitOfWork.CompleteAsync(cancellationToken);
        }

        public Task LoadContextToWorkflowAsync(
            ICollection<DocumentInfo> documentInfos,
            Workflow workflow,
            List<Page> allPages,
            Func<string?, Task>? onDocumentProcessed = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}

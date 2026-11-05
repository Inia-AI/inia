using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.UserData.Interfaces;

namespace IniaAi.Application.Documents;

/// <summary>
/// The one way a document enters standalone processing: the request stamp, its commit, and
/// the hand-off to the worker, in that order — the request is queued only once the document
/// it names is on disk as <c>Queued</c>. The hand-off ignores the request's cancellation: a
/// client that disconnects must not leave a committed <c>Queued</c> document queued nowhere.
/// </summary>
public static class DocumentProcessingStarter
{
    public static async Task StartAsync(
        IUserDataUnitOfWork userData,
        IWorkQueue<DocumentProcessingRequest> queue,
        Document document,
        string userId,
        CancellationToken cancellationToken)
    {
        document.RequestProcessing(DateTime.UtcNow);
        _ = await userData.CompleteAsync(cancellationToken);
        await queue.EnqueueAsync(new DocumentProcessingRequest(document.OrganizationId, document.Id, userId), CancellationToken.None);
    }
}

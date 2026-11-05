using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Services.Models.Jobs;

namespace IniaAi.Core.Documents;

/// <summary>
/// The stage between loading a run's documents and processing them. It may take a document out
/// of the run with <see cref="DocumentInfo.ExcludeFromRun"/>, may set the class of a document it
/// keeps, and may refuse the run by throwing; the job executor then processes only the documents
/// still in the run. The platform's own stage, <see cref="PassThroughRunInputStage"/>, keeps
/// every document as it is; a module registers its own stage in place of it.
/// </summary>
public interface IRunInputStage
{
    Task RunAsync(
        Job job,
        string? workflowExecutionId,
        IReadOnlyList<DocumentInfo> documentInfos,
        IUserDataUnitOfWork userDataUnitOfWork,
        CancellationToken cancellationToken);
}

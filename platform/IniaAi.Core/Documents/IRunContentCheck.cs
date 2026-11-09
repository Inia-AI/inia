using IniaAi.Domain.Entities.Documents;
using IniaAi.Services.Models.Jobs;

namespace IniaAi.Core.Documents;

/// <summary>
/// The check between processing a run's documents and running its blocks. A refusal fails the run with the refusal's
/// kind and detail before any block calls a model. The platform's own check, <see cref="PassRunContentCheck"/>, refuses
/// nothing; a module registers its own in place of it.
/// </summary>
public interface IRunContentCheck
{
    Task<RunContentRefusal?> CheckAsync(Job job, IReadOnlyList<DocumentInfo> documentInfos, CancellationToken cancellationToken);
}

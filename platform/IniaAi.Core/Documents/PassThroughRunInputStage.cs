using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Services.Models.Jobs;

namespace IniaAi.Core.Documents;

/// <summary>The platform's own run-input stage: every document the run loads stays run input.</summary>
public sealed class PassThroughRunInputStage : IRunInputStage
{
    public Task RunAsync(
        Job job,
        string? workflowExecutionId,
        IReadOnlyList<DocumentInfo> documentInfos,
        IUserDataUnitOfWork userDataUnitOfWork,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

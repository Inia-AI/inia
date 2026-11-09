using IniaAi.Domain.Entities.Documents;
using IniaAi.Services.Models.Jobs;

namespace IniaAi.Core.Documents;

/// <summary>The platform's own run content check: every run goes on to its blocks.</summary>
public sealed class PassRunContentCheck : IRunContentCheck
{
    public Task<RunContentRefusal?> CheckAsync(Job job, IReadOnlyList<DocumentInfo> documentInfos, CancellationToken cancellationToken)
    {
        return Task.FromResult<RunContentRefusal?>(null);
    }
}

using IniaAi.Common.Interfaces.Services.Runs;

namespace IniaAi.Testing.DependencyInjection.Mocks;

/// <summary>
/// Records each run a final state asked to settle, for the suites that assert a transition reaches
/// the seam.
/// </summary>
public class RecordingRunSettlement : IRunSettlement
{
    public List<(string ExecutionId, string OrganizationId)> Settled { get; } = [];

    public Task SettleAsync(string workflowExecutionId, string organizationId)
    {
        Settled.Add((workflowExecutionId, organizationId));

        return Task.CompletedTask;
    }
}

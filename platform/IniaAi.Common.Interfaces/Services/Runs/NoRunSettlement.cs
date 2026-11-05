namespace IniaAi.Common.Interfaces.Services.Runs;

public sealed class NoRunSettlement : IRunSettlement
{
    public Task SettleAsync(string workflowExecutionId, string organizationId)
    {
        return Task.CompletedTask;
    }
}

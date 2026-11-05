namespace IniaAi.Application.Common.DailyAllowances;

public sealed class NoDailyLimit : IDailyAllowance
{
    public Task<DailyAllowanceTake?> TakeAsync(DailyAllowanceRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult<DailyAllowanceTake?>(null);
    }

    public Task GiveBackAsync(DailyAllowanceTake take, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

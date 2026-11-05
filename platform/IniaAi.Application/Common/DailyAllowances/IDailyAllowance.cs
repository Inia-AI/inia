namespace IniaAi.Application.Common.DailyAllowances;

/// <summary>
/// How many requests of a kind a user may make a UTC day. The platform sets no limit; a closed
/// module may set one.
/// </summary>
public interface IDailyAllowance
{
    /// <summary>
    /// Takes one request from the user's allowance for today, in one atomic step. Null when no limit
    /// applies to the user; otherwise <see cref="DailyAllowanceTake.Taken"/> says whether one was left.
    /// </summary>
    Task<DailyAllowanceTake?> TakeAsync(DailyAllowanceRequest request, CancellationToken cancellationToken);

    /// <summary>Gives back a request that <see cref="TakeAsync"/> took, on the day it was taken.</summary>
    Task GiveBackAsync(DailyAllowanceTake take, CancellationToken cancellationToken);
}

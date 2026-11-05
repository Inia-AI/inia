namespace IniaAi.Domain.Entities.Validation;

/// <summary>
/// The partial index that serves the retention sweep's window query. Its filter states the
/// sweep's rule, <c>ValidationRunAutomaticTrim.Applies</c>, as raw SQL, and is deliberately a
/// superset of it, because neither remaining term can go in a filter: the adjudicated outcome is
/// a disjunction, and a SQL Server filtered index accepts only a conjunction; the accepted run the
/// sweep also skips is a <c>bool</c>, which EF renders as <c>CAST(0 AS bit)</c> rather than the
/// literal such an index matches on. The sweep's query also skips a pass compared against pasted
/// text, which holds nothing to delete; that term stays out of the filter, which is still a
/// superset of what the query reads. A tightening of the rule belongs here too, followed by a new
/// migration.
/// </summary>
public static class ValidationRunAwaitingTrimIndex
{
    public const string NAME = "IX_ValidationRuns_AwaitingTrim";

    public static string Filter(bool postgreSql)
    {
        string open = postgreSql ? "\"" : "[";
        string close = postgreSql ? "\"" : "]";
        string column(string name) => $"{open}{name}{close}";

        return $"{column(nameof(ValidationRun.PayloadTrimmedAt))} IS NULL"
            + $" AND {column(nameof(ValidationRun.Status))} = {(int)ValidationRunStatus.Completed}"
            + $" AND {column(nameof(ValidationRun.Outcome))} = {(int)ValidationRunOutcome.Pass}"
            + $" AND {column(nameof(ValidationRun.UnsettledFieldCount))} = 0";
    }
}

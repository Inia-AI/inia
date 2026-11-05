namespace IniaAi.Configuration.Platform;

/// <summary>
/// How often the usage-totals sweep adds new token usage ledger rows to the daily and per-execution
/// totals. A usage read adds the rows the sweep has not reached, so the interval sets how much a
/// read sums from the ledger, never whether it is exact.
/// </summary>
public class TokenUsageTotalsSettings
{
    public const string SECTION_NAME = "PlatformSettings:TokenUsageTotals";

    public bool Enabled { get; set; } = true;

    public int SweepIntervalSeconds { get; set; } = 30;
}

namespace IniaAi.Configuration.Platform;

/// <summary>
/// What the workflow-version retention sweep is allowed to do. <see cref="Disabled"/> (the default)
/// never touches any payload. <see cref="ReportOnly"/> logs what it would clear without changing
/// anything — the recommended first step. <see cref="Trim"/> permanently clears old payloads;
/// clearing is irreversible, so an operator should run <see cref="ReportOnly"/> and take a backup
/// before enabling it.
/// </summary>
public enum WorkflowVersionRetentionMode
{
    Disabled,
    ReportOnly,
    Trim,
}

/// <summary>
/// Deployment-static policy for reclaiming storage from workflow-version history. The sweep keeps the
/// metadata of every version (so comparison and provenance stay correct) and only clears the heavy
/// saved JSON of versions outside the keep window. Disabled by default; an on-prem operator opts in
/// per deployment. There is no per-organization override — changing this needs a redeploy, which is
/// deliberate for an irreversible operation.
/// </summary>
public class WorkflowVersionRetentionSettings
{
    public const string SECTION_NAME = "PlatformSettings:WorkflowVersionRetention";

    public WorkflowVersionRetentionMode Mode { get; set; } = WorkflowVersionRetentionMode.Disabled;

    public int KeepLastNPayloads { get; set; } = 20;

    public int KeepNewerThanDays { get; set; } = 180;

    public DayOfWeek TrimDayOfWeek { get; set; } = DayOfWeek.Sunday;

    public int TrimHourUtc { get; set; } = 2;

    public int MaxPayloadsTrimmedPerRun { get; set; } = 500;
}

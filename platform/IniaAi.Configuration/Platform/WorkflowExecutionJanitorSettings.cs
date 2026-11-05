namespace IniaAi.Configuration.Platform;

/// <summary>
/// Deployment-static policy for the stuck-execution janitor: executions still in an active
/// state past <see cref="MaxLifetimeHours"/> are presumed stranded by a dead host and are
/// failed through the guarded terminal write. The lifetime is deliberately generous —
/// hours-scale, well past agent-block deadlines times passes plus document processing — and
/// deliberately under the runtime's 24-hour retention reaper, so cleanup still finds the
/// leaked runs it must complete.
/// </summary>
public class WorkflowExecutionJanitorSettings
{
    public const string SECTION_NAME = "PlatformSettings:WorkflowExecutionJanitor";

    public bool Enabled { get; set; } = true;

    public int MaxLifetimeHours { get; set; } = 12;

    public int SweepIntervalMinutes { get; set; } = 30;

    /// <summary>
    /// Whether each process records its start and heartbeats it, and a sweep every minute fails the
    /// active runs of a process that stopped, within minutes rather than at
    /// <see cref="MaxLifetimeHours"/>. Off by default.
    /// </summary>
    public bool HostLossRecovery { get; set; }
}

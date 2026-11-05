namespace IniaAi.Configuration.Platform;

/// <summary>
/// Deployment-static policy for the session-bundle retention sweep: a bundle taken into custody
/// more than <see cref="MaxAgeDays"/> ago is deleted with its bytes, whatever became of the
/// execution that produced it. The sweep is the backstop, not the main path — an execution
/// releases its own bundles the moment it reaches a terminal — so it only ever collects what a
/// crashed or stranded execution left behind.
/// <para>
/// The window is generous against the stuck-execution janitor's twelve hours: an execution the
/// janitor never reached is already an anomaly, and a week leaves room to look at one before
/// its evidence goes. The interval is short against the window on purpose — the sweep reads one
/// indexed column and usually deletes nothing, so running it often costs nothing and keeps the
/// overshoot past the window down to an hour.
/// </para>
/// </summary>
public class AgentRunSessionBundleReaperSettings
{
    public const string SECTION_NAME = "PlatformSettings:AgentRunSessionBundleReaper";

    public bool Enabled { get; set; } = true;

    public int MaxAgeDays { get; set; } = 7;

    public int SweepIntervalMinutes { get; set; } = 60;
}

using System.ComponentModel.DataAnnotations;

namespace IniaAi.Configuration.Platform;

/// <summary>
/// Deployment-wide settings of agent conversations, whatever their purpose. Each purpose sets its
/// own retention window and running-turn cap; these govern the sweeps that apply them.
/// </summary>
public class AgentConversationSettings
{
    public const string SECTION_NAME = "PlatformSettings:AgentConversations";

    /// <summary>How often retention ends idle conversations and deletes ended ones.</summary>
    [Range(1, 1440)]
    public int SweepIntervalMinutes { get; set; } = 60;

    /// <summary>
    /// How often the host that runs turns settles overdue turns and starts waiting ones whose
    /// predecessor settled.
    /// </summary>
    [Range(1, 1440)]
    public int TurnSweepIntervalMinutes { get; set; } = 2;

    /// <summary>
    /// How long past its purpose's deadline a dispatched turn may stay in flight before the sweep
    /// fails it and cancels its run. The margin covers admission, polling and settle.
    /// </summary>
    [Range(1, 1440)]
    public int OverdueMarginMinutes { get; set; } = 10;

    /// <summary>
    /// The name this host stamps on the turns it runs, so startup recovery abandons only its own.
    /// Empty takes the machine name and the application name, which survives a restart of the
    /// same host; set it where several hosts share a machine name.
    /// </summary>
    [StringLength(100)]
    public string HostInstanceId { get; set; } = string.Empty;
}

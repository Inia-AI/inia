using System.ComponentModel.DataAnnotations;

namespace IniaAi.Configuration.Platform;

/// <summary>
/// The agent beside the workflow editor, per deployment. The feature is on only when
/// <see cref="Enabled"/> is set and the agent runtime is enabled too
/// (<see cref="AgentRuntimeSettings.Enabled"/>); either off hides it.
/// </summary>
public class EditorAgentSettings
{
    public const string SECTION_NAME = "PlatformSettings:EditorAgent";
    public const string DEFAULT_EXPERT_MODEL = "us.anthropic.claude-opus-5-5";

    public bool Enabled { get; set; } = false;

    /// <summary>
    /// A conversation ends this many days after its last message and is deleted by the next
    /// retention sweep (<see cref="AgentConversationSettings.SweepIntervalMinutes"/>).
    /// </summary>
    [Range(1, 365)]
    public int RetentionDays { get; set; } = 7;

    /// <summary>
    /// How many messages one user may have running at once, across their editor conversations.
    /// A message sent while its conversation is busy waits and does not count.
    /// </summary>
    [Range(1, 100)]
    public int MaxRunningTurnsPerUser { get; set; } = 2;

    /// <summary>
    /// Whether a message continues the previous message's agent session. Off by default: until
    /// the runtime reports a continuation's own cost rather than the whole session's
    /// (agent-runtime PR 214), a resumed message is over-billed. Off, every message starts a fresh
    /// session with the earlier messages repeated in its prompt, and no session bundle is kept.
    /// </summary>
    public bool ResumeSessions { get; set; } = false;

    /// <summary>
    /// The largest workflow, as export JSON with test-run outputs, a message may stage. A
    /// bigger one is refused before anything is stored. Also the largest past report run the
    /// agent is given: a bigger run is left out and its header says so.
    /// </summary>
    [Range(1, 1024)]
    public int MaxDraftMegabytes { get; set; } = 50;

    /// <summary>
    /// The most a message stages of its past reports' documents (originals and page text),
    /// across every report it reads. Held under the runtime's input budget once the workflow's
    /// own files are counted; what does not fit is listed in the report's header.
    /// </summary>
    [Range(0, 1024)]
    public int MaxReportDocumentsMegabytesPerMessage { get; set; } = 150;

    /// <summary>The largest original document of a past report that is staged; a bigger one is listed as left out.</summary>
    [Range(1, 1024)]
    public int MaxReportDocumentMegabytes { get; set; } = 50;

    /// <summary>
    /// Per-message fuses. Each is clamped to the runtime's ceiling when the runtime has published
    /// one, so a deployment with a lower ceiling never sees a refused start.
    /// </summary>
    [Range(typeof(decimal), "0.01", "1000")]
    public decimal MaxBudgetUsdPerMessage { get; set; } = 3m;

    /// <summary>
    /// An organization's daily batch of agent spend, in USD, when its own
    /// <c>Organization.EditorAgentDailySpendBatchUsd</c> is unset. Once the organization's
    /// spend today (UTC) reaches its batches, the next message warns, and the user may unlock
    /// another batch for the day. Running messages always finish.
    /// </summary>
    [Range(typeof(decimal), "0.01", "100000")]
    public decimal DailySpendBatchUsd { get; set; } = 50m;

    [Range(1, 1000)]
    public int MaxTurnsPerMessage { get; set; } = 60;

    [Range(1, 240)]
    public int DeadlineMinutesPerMessage { get; set; } = 15;

    /// <summary>How long a message waits for a free runtime slot before it fails.</summary>
    [Range(0, 3600)]
    public int AdmissionPatienceSeconds { get; set; } = 120;

    /// <summary>The reasoning effort every message runs at.</summary>
    [Required]
    public string Effort { get; set; } = "medium";

    /// <summary>
    /// Used only when the runtime allowlist holds neither the platform's advanced nor its basic
    /// model: a new conversation then starts on the first allowed model whose id contains this
    /// text, or the first allowed model when none does.
    /// </summary>
    [Required]
    public string DefaultModelMatch { get; set; } = "sonnet-5";

    /// <summary>
    /// The Expert model: listed first in the picker, above the Advanced and Standard tiers, as the
    /// runtime allowlist names it. Hidden when the allowlist has no form of it, or when it is the
    /// same model as a tier. Empty hides the slot. Never the model a new conversation starts on.
    /// </summary>
    public string ExpertModel { get; set; } = DEFAULT_EXPERT_MODEL;

    /// <summary>
    /// How often, in milliseconds, the panel's live steps are refreshed from the agent's trace
    /// while a message runs. Zero falls back to the shared feed's pace (every few seconds).
    /// </summary>
    [Range(0, 60_000)]
    public int LiveStepsIntervalMilliseconds { get; set; } = 1000;
}

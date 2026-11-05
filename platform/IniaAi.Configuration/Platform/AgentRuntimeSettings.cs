using IniaAi.Common.Enums;
using IniaAi.Common.Extensions;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Configuration.Platform;

public class AgentRuntimeSettings : IValidatableObject
{
    /// <summary>
    /// The section name in appsettings.json.
    /// </summary>
    public const string SectionName = "PlatformSettings:AgentRuntime";

    public const string DEFAULT_ADVANCED_MODEL = "claude-opus-5-5";

    public const string DEFAULT_BASIC_MODEL = "claude-sonnet-5-5";

    /// <summary>
    /// When true, the agent runtime sidecar client is registered and runs can be
    /// started on the external agent runtime. Off by default while the feature is
    /// incubating; also the mixed-version authoring gate — agent blocks can only be
    /// saved with this on, and it is flipped per environment only after every host
    /// runs a build whose serializers know the Agent block type.
    /// </summary>
    public bool Enabled { get; set; } = false;

    public string BaseUrl { get; set; } = "http://localhost:8130";

    public string AuthToken { get; set; } = string.Empty;

    /// <summary>
    /// Explicit opt-out of sidecar authentication for local development against a sidecar
    /// running with AUTH_DISABLED. Never set this in a deployed environment: without it,
    /// an enabled feature with no AuthToken fails at startup instead of silently sending
    /// unauthenticated requests.
    /// </summary>
    public bool AllowUnauthenticatedSidecar { get; set; } = false;

    [Range(1, int.MaxValue)]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Validity of the per-run JWT minted for the agent runtime. Deliberately long
    /// (default 24h) so a paused run awaiting user input can resume without re-minting.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int RunTokenValidityHours { get; set; } = 24;

    /// <summary>
    /// The runtime's pinned model set mirrored into platform config: agent blocks may
    /// only select these model ids, enforced at workflow save time. The runtime's own
    /// allowlist (a 400 on run start) is the backstop when this mirror drifts.
    /// </summary>
    public List<string> AllowedModels { get; set; } = [];

    /// <summary>
    /// The model an Agent or Verification block runs when it names no model of its own and its
    /// advanced tier is on, unless the organization's
    /// <c>ModelRouting:TenantModelOverrides:{org}:AgentAdvancedModel</c> names one; blank falls back
    /// to <see cref="DEFAULT_ADVANCED_MODEL"/>. Any provider's id of an allowlisted model works: it
    /// resolves to the allowlist's form when a run starts, like a block's own model. Not checked
    /// against <see cref="AllowedModels"/> here: a deployment whose list lacks it keeps starting,
    /// and the save and submit gates refuse a block on this tier with the reason.
    /// </summary>
    public string AdvancedModel { get; set; } = DEFAULT_ADVANCED_MODEL;

    /// <summary>
    /// The model an Agent or Verification block runs when it names no model of its own and its
    /// advanced tier is off, unless the organization's <c>AgentBasicModel</c> override names one;
    /// blank falls back to <see cref="DEFAULT_BASIC_MODEL"/>. Resolves like <see cref="AdvancedModel"/>.
    /// </summary>
    public string BasicModel { get; set; } = DEFAULT_BASIC_MODEL;

    /// <summary>
    /// The effort vocabulary, derived from <see cref="ModelEffort"/> so the editor's dropdown
    /// and save-time validation track the one enum. The runtime's own validation (a 400 on run
    /// start) is the backstop when the enum drifts from it. There is deliberately no platform
    /// default: a block with no explicit effort omits it from the start request and the
    /// runtime/SDK default applies.
    /// </summary>
    public static readonly IReadOnlyList<string> EffortLevels =
        [.. Enum.GetValues<ModelEffort>().Select(static effort => effort.GetEnumMember())];

    /// <summary>
    /// Externally reachable MCP server base URL handed to worker runs (the client's nullable
    /// mcpServerUrl start field). Unused until the MCP batch — deployed environments configure
    /// it ahead so enabling MCP is a flag flip, not a config rollout.
    /// </summary>
    public string? McpServerUrl { get; set; }

    /// <summary>
    /// How long a consumer waits out capacity refusals before giving up on ever being admitted.
    /// Spent before the run exists, so it never eats the run's own deadline. Zero disables
    /// patience and makes the first refusal fatal.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int AdmissionPatienceSeconds { get; set; } = 600;

    /// <summary>
    /// How many runs one consumer may burn on a single piece of work when each failure is
    /// retryable infrastructure trouble (see <see cref="Common.Interfaces.Services.AgentRuntime.AgentRunSubtypes.IsRetryable"/>).
    /// One disables retries.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int MaxRunPasses { get; set; } = 3;

    /// <summary>
    /// How long an Agent or Verification block waits before the one fresh pass a run failing
    /// <c>upstream_error</c> buys (the model API failed on its side), inside <see cref="MaxRunPasses"/>.
    /// The 2026-10-06 Bedrock outage lasted about 40 minutes, and each failed run had already spent
    /// 5 to 8 of them in the SDK's own retries. Zero turns the outage pass off.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int UpstreamErrorRetryDelaySeconds { get; set; } = 600;

    /// <summary>
    /// How many consecutive failed polls a live run survives before the consumer gives up on
    /// it. A poll failure says nothing about the run, so voiding one on the first errored poll
    /// throws away spend the run may be seconds from earning. Zero restores that behaviour.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int PollErrorTolerance { get; set; } = 3;

    /// <summary>
    /// How much longer a consumer keeps polling than the run deadline it declared. The margin
    /// covers admission, the runtime's own teardown, and poll spacing, so the consumer sees the
    /// runtime's verdict instead of racing it.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int RunDeadlineMarginSeconds { get; set; } = 120;

    /// <summary>
    /// How long a consumer keeps polling a run whose deadline it does not know, because the block
    /// declared none and the runtime's own default governs it. This bounds the consumer's
    /// patience only: it never rides the start request and never limits the run, so it is not a
    /// mirror of any runtime fuse and must not be tuned as if it were one.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int MaxPollDeadlineMinutes { get; set; } = 30;

    /// <summary>
    /// The most Agent block copies one loop may expand to in a run. The list a loop runs over is
    /// known only at run time, often from a model's answer, and every copy buys its own run, so a
    /// loop that would make more copies fails before any of them starts.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int MaxLoopAgentCopies { get; set; } = 20;

    /// <summary>
    /// When true, chat turns (agent conversations such as the workflow editor agent) ask the
    /// runtime for its chat admission class, which keeps a reserve of run slots for them and caps
    /// how many they may hold. Workflow runs never send a class. Off by default: a runtime older
    /// than the agent-runtime release with its ADR 0142 rejects the field
    /// with 400, so turn this on only once every runtime the platform talks to runs that release.
    /// </summary>
    public bool SendAdmissionClass { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Enabled)
        {
            yield break;
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) || !Uri.TryCreate(BaseUrl, UriKind.Absolute, out Uri? parsedBaseUrl))
        {
            yield return new ValidationResult(
                $"{SectionName}:{nameof(BaseUrl)} must be an absolute URL when the agent runtime is enabled",
                [nameof(BaseUrl)]);
        }
        else if (CarriesApiVersionSegment(parsedBaseUrl))
        {
            yield return new ValidationResult(
                $"{SectionName}:{nameof(BaseUrl)} must not include the runtime's API version segment — the client "
                + "appends it, so a versioned base URL doubles it into a 404. Configure the host (and any proxy "
                + "path prefix) only.",
                [nameof(BaseUrl)]);
        }

        if (string.IsNullOrWhiteSpace(AuthToken) && !AllowUnauthenticatedSidecar)
        {
            yield return new ValidationResult(
                $"{SectionName}:{nameof(AuthToken)} must be set when the agent runtime is enabled, " +
                $"or {nameof(AllowUnauthenticatedSidecar)}=true must be opted into explicitly for local development",
                [nameof(AuthToken)]);
        }

        if (AllowedModels is not { Count: > 0 } || AllowedModels.Any(string.IsNullOrWhiteSpace))
        {
            yield return new ValidationResult(
                $"{SectionName}:{nameof(AllowedModels)} must list at least one non-blank runtime model id when the agent runtime is enabled",
                [nameof(AllowedModels)]);
        }

        if (McpServerUrl is not null
            && (!Uri.TryCreate(McpServerUrl, UriKind.Absolute, out Uri? parsedMcpUrl)
                || (parsedMcpUrl.Scheme != Uri.UriSchemeHttp && parsedMcpUrl.Scheme != Uri.UriSchemeHttps)))
        {
            yield return new ValidationResult(
                $"{SectionName}:{nameof(McpServerUrl)} must be an absolute http(s) URL when set",
                [nameof(McpServerUrl)]);
        }
    }

    private static bool CarriesApiVersionSegment(Uri baseUrl)
    {
        string[] segments = baseUrl.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        string lastSegment = segments[^1];
        return lastSegment.Length > 1
            && lastSegment[0] == 'v'
            && lastSegment[1..].All(char.IsAsciiDigit);
    }
}

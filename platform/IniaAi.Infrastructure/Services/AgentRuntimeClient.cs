using ErrorOr;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Infrastructure.Persistence.Serialization;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// HTTP client for the agent runtime sidecar. Maps sidecar status codes onto ErrorOr:
/// 404 → NotFound, 409 → a per-code conflict, 400 → Validation, 503 → a typed admission
/// refusal (<see cref="AgentRunAdmissionErrors"/>) carrying the honoured Retry-After,
/// anything else non-2xx → Failure. The HttpClient arrives fully configured (base address,
/// timeout, bearer auth) from the registration site, which binds and validates
/// AgentRuntimeSettings at startup.
/// </summary>
public partial class AgentRuntimeClient : IAgentRuntimeClient
{
    // The sidecar 404s any unversioned path except /healthz, so the API version belongs to the
    // client that speaks it, not to the configured base URL.
    private const string RUNS_PATH = "v1/runs";
    private const string LIMITS_PATH = "v1/limits";
    private const string ENVIRONMENT_PATH = "v1/environment";
    private const string CANCEL_SEGMENT = "cancel";
    private const string INPUT_SEGMENT = "input";
    private const string COMPLETE_SEGMENT = "complete";
    private const string FILES_SEGMENT = "files";
    private const string CHECKPOINT_SEGMENT = "checkpoint";

    // The runtime returns the stored bundle's digest in this header rather than in a body it
    // would have to buffer to compose (run-files-routes.ts).
    private const string CHECKPOINT_SHA256_HEADER = "x-checkpoint-sha256";

    private const string ERROR_NOT_FOUND = AgentRunLifecycleErrors.NOT_FOUND;
    private const string ERROR_CONFLICT = "AgentRun.Conflict";
    private const string ERROR_PAYLOAD_CONFLICT = "AgentRun.PayloadConflict";
    private const string ERROR_STALE_RESUME_TOKEN = "AgentRun.StaleResumeToken";
    private const string ERROR_NOT_AWAITING_INPUT = "AgentRun.NotAwaitingInput";
    private const string ERROR_UNKNOWN_QUESTION = "AgentRun.UnknownQuestion";
    private const string ERROR_VALIDATION = "AgentRun.Validation";
    private const string ERROR_FAILURE = "AgentRun.Failure";
    private const string ERROR_INVALID_RESPONSE = "AgentRun.InvalidResponse";
    private const string ERROR_UNREACHABLE = "AgentRun.Unreachable";
    private const string ERROR_TIMEOUT = "AgentRun.Timeout";
    private const string ERROR_COMPLETED = AgentRunLifecycleErrors.COMPLETED;
    private const string ERROR_NOT_COMPLETABLE = "AgentRun.NotCompletable";
    private const string ERROR_INVALID_DOCUMENT = "AgentRun.InvalidDocument";
    private const string ERROR_DOCUMENTS_OVER_BUDGET = "AgentRun.DocumentsOverBudget";
    private const string CONFLICT_CODE_PAYLOAD = "payload_conflict";
    private const string CONFLICT_CODE_STALE_RESUME_TOKEN = "stale_resume_token";
    private const string CONFLICT_CODE_NOT_AWAITING_INPUT = "not_awaiting_input";
    private const string CONFLICT_CODE_UNKNOWN_QUESTION = "unknown_question";
    private const string CONFLICT_CODE_NOT_COMPLETABLE = "not_completable";
    private const string STATUS_METADATA_KEY = "status";
    private const string ADMISSION_BODY_SHUTTING_DOWN = "shutting down";
    private const string ADMISSION_BODY_CAPACITY = "capacity";
    private const int ADMISSION_REASON_MAX_CHARS = 200;
    private const double RETRY_AFTER_JITTER_RATIO = 0.2;

    // The runtime clamps its own advertisement at 300 s (admission-queue.ts MAX_RETRY_AFTER_SECONDS),
    // so anything past this is out of contract rather than a saturated pool talking.
    private static readonly TimeSpan _retryAfterCap = TimeSpan.FromSeconds(300);

    private static readonly ConditionalWeakTable<AgentRunStartRequest, ArrayBufferWriter<byte>> _serializedStartBodies = new();

    // Mirrors the runtime's admission rules (run-documents.ts): staged-input names share one
    // pattern and one namespace across documents and skill files, "manifest.json" is the skills
    // manifest's own name, and the entry and byte ceilings are checked against decoded sizes
    // before any HTTP so an over-budget request fails without uploading 200 MiB first.
    private const string RESERVED_MANIFEST_NAME = "manifest.json";
    private const string OUTPUT_FORMAT_TYPE_JSON_SCHEMA = "json_schema";
    private const string ADMISSION_CLASS_WORKFLOW = "workflow";
    private const string ADMISSION_CLASS_CHAT = "chat";
    private const string ADMISSION_REASON_CHAT_DISABLED = "chat_disabled";
    private const string CONTENT_TYPE_JSON = "application/json";
    private const string CHARSET_UTF8 = "utf-8";
    private const int OUTPUT_SCHEMA_MAX_CHARS = 256 * 1024;
    [GeneratedRegex(@"^[A-Za-z0-9._ -]{1,200}$")]
    private static partial Regex StagedInputNamePattern();

    private readonly HttpClient _httpClient;
    private readonly ILogger<AgentRuntimeClient> _logger;

    public AgentRuntimeClient(HttpClient httpClient, ILogger<AgentRuntimeClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public Task<ErrorOr<AgentRunMonitor>> StartRunAsync(
        string executionId,
        string prompt,
        string runToken,
        CancellationToken cancellationToken = default)
    {
        return StartRunAsync(new AgentRunStartRequest(executionId, prompt, runToken), cancellationToken);
    }

    public async Task<ErrorOr<AgentRunMonitor>> StartRunAsync(
        AgentRunStartRequest request,
        CancellationToken cancellationToken = default)
    {
        if (ValidateStartInputs(request) is Error invalid)
        {
            _logger.LogError(
                "Refused an agent run start for execution {ExecutionId}: {Reason}",
                request.ExecutionId, invalid.Description);
            return invalid;
        }

        ArrayBufferWriter<byte> serializedBody = _serializedStartBodies.GetValue(request, SerializeStartBody);

        ErrorOr<(AgentRunMonitor Monitor, HttpStatusCode StatusCode)> response = await SendAsync(
            () => _httpClient.PostAsync(RUNS_PATH, MeasuredContent(serializedBody), cancellationToken),
            request.ExecutionId,
            cancellationToken);

        // Only an admission refusal re-posts this instance, so any other outcome is the last
        // use of the cached body — dropping it now releases what can be ~85 MiB of base64 for
        // a continuation start instead of pinning it for as long as the caller polls the run.
        if (!response.IsError || !AgentRunAdmissionErrors.IsAdmissionRefusal(response.FirstError.Code))
        {
            _ = _serializedStartBodies.Remove(request);
        }

        return response.Then(r => r.Monitor with { AlreadyRegistered = r.StatusCode == HttpStatusCode.OK });
    }

    /// <summary>
    /// The caller's admission patience re-posts the same request instance until a slot frees, and
    /// each attempt would otherwise rebuild the whole body — re-base64-ing every skill file and
    /// re-copying every document. Keyed by instance, so a retry pass, which builds its own
    /// request, still serializes fresh.
    /// </summary>
    private static ArrayBufferWriter<byte> SerializeStartBody(AgentRunStartRequest request)
    {
        // Sized up front: the body is dominated by base64 (4/3 of the decoded bytes), and the
        // default empty buffer would double its way up through LOH copies to reach it.
        long decodedBytes = AgentRunInputCapacity.StartRequestBytes(request)
            + (request.Continuation?.SessionBundle.Length ?? 0);
        int sizeHint = (int)Math.Min(int.MaxValue - 1024, (decodedBytes * 4 / 3) + 16_384);

        return SerializeToBuffer(sizeHint, new StartRunRequestBody(
            request.ExecutionId,
            request.Prompt,
            request.RunToken,
            request.Documents.Select(static document => new DocumentBody(
                document.Name,
                document.Content,
                document.MediaType,
                document.DocumentClass)).ToList(),
            request.SkillFiles.Select(static skill => new SkillFileBody(
                skill.Name,
                Convert.ToBase64String(Encoding.UTF8.GetBytes(skill.Content)),
                string.IsNullOrWhiteSpace(skill.Description) ? null : skill.Description,
                skill.Assets.Count == 0
                    ? null
                    : skill.Assets.Select(static asset => new SkillAssetBody(
                        asset.Name,
                        Convert.ToBase64String(Encoding.UTF8.GetBytes(asset.Content)))).ToList())).ToList(),
            request.Model,
            request.Effort,
            request.SystemPrompt,
            request.OutputSchema is null
                ? null
                : new OutputFormatBody(OUTPUT_FORMAT_TYPE_JSON_SCHEMA, request.OutputSchema),
            request.McpServerUrl,
            request.DeadlineMs,
            request.MaxBudgetUsd,
            request.MaxTurns,
            request.MaxRetainedOutputBytes,
            request.Continuation is null
                ? null
                : new ContinuationBody(
                    new SessionBundleBody(request.Continuation.SessionBundleSha256, request.Continuation.SessionBundle),
                    request.Continuation.OutFiles.Select(static file => new RecarriedFileBody(
                        file.Path,
                        file.Content,
                        file.Sha256)).ToList()),
            AdmissionClassOf(request.AdmissionClass)));
    }

    private static string? AdmissionClassOf(AgentRunAdmissionClass? admissionClass)
    {
        return admissionClass switch
        {
            null => null,
            AgentRunAdmissionClass.Workflow => ADMISSION_CLASS_WORKFLOW,
            AgentRunAdmissionClass.Chat => ADMISSION_CLASS_CHAT,
            _ => throw new ArgumentOutOfRangeException(nameof(admissionClass), admissionClass, null),
        };
    }

    public async Task<ErrorOr<IReadOnlyList<AgentRunFileEntry>>> ListRunFilesAsync(
        string executionId,
        CancellationToken cancellationToken = default)
    {
        ErrorOr<byte[]> response = await SendRawAsync(
            () => _httpClient.GetAsync($"{RunPath(executionId)}/{FILES_SEGMENT}", cancellationToken),
            executionId,
            cancellationToken);

        return response.Then(bytes => ParseManifest(bytes, executionId));
    }

    private ErrorOr<IReadOnlyList<AgentRunFileEntry>> ParseManifest(byte[] bytes, string executionId)
    {
        IReadOnlyList<AgentRunFileEntry>? files = null;
        try
        {
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("files", out JsonElement filesElement))
            {
                files = filesElement.Deserialize<List<AgentRunFileEntry>>(JsonDefaults.Options);
            }
        }
        catch (JsonException)
        {
        }

        if (files is null)
        {
            _logger.LogError(
                "Agent runtime returned an unparseable file manifest for execution {ExecutionId}",
                executionId);
            return Error.Failure(ERROR_INVALID_RESPONSE, "Agent runtime returned an unparseable file manifest");
        }

        return ErrorOrFactory.From(files);
    }

    public Task<ErrorOr<byte[]>> GetRunFileAsync(
        string executionId,
        string path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult<ErrorOr<byte[]>>(Error.Validation(
                ERROR_VALIDATION, "A run file path is required — use a path from the file manifest"));
        }

        // Escaping alone keeps '.'/'..' intact (unreserved), and dot segments normalize into the
        // URL before the sidecar sees them — "../other/files/x" would read another run's files.
        string[] segments = path.Split('/');
        if (Array.Exists(segments, static segment => segment.Length == 0 || segment is "." or ".."))
        {
            return Task.FromResult<ErrorOr<byte[]>>(Error.Validation(
                ERROR_VALIDATION,
                "A run file path must be a manifest-relative path without empty, '.' or '..' segments"));
        }

        string escapedPath = string.Join('/', segments.Select(Uri.EscapeDataString));

        return SendRawAsync(
            () => _httpClient.GetAsync($"{RunPath(executionId)}/{FILES_SEGMENT}/{escapedPath}", cancellationToken),
            executionId,
            cancellationToken);
    }

    public Task<ErrorOr<AgentRunCheckpoint>> GetRunCheckpointAsync(
        string executionId,
        CancellationToken cancellationToken = default)
    {
        return SendCoreAsync(
            () => _httpClient.GetAsync($"{RunPath(executionId)}/{CHECKPOINT_SEGMENT}", cancellationToken),
            executionId,
            ReadCheckpointSuccessAsync,
            cancellationToken);
    }

    private async Task<ErrorOr<AgentRunCheckpoint>> ReadCheckpointSuccessAsync(
        HttpResponseMessage response,
        string executionId,
        CancellationToken cancellationToken)
    {
        if (!response.Headers.TryGetValues(CHECKPOINT_SHA256_HEADER, out IEnumerable<string>? digests)
            || digests.FirstOrDefault() is not string declaredSha256
            || string.IsNullOrWhiteSpace(declaredSha256))
        {
            _logger.LogError(
                "Agent runtime served a session bundle for execution {ExecutionId} without its {Header} header",
                executionId, CHECKPOINT_SHA256_HEADER);

            return Error.Failure(
                ERROR_INVALID_RESPONSE,
                $"Agent runtime served a session bundle without its {CHECKPOINT_SHA256_HEADER} header");
        }

        byte[] content = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        return ErrorOrFactory.From(new AgentRunCheckpoint(content, declaredSha256));
    }

    public async Task<ErrorOr<AgentRunMonitor>> CompleteRunAsync(
        string executionId,
        CancellationToken cancellationToken = default)
    {
        ErrorOr<(AgentRunMonitor Monitor, HttpStatusCode StatusCode)> response = await SendAsync(
            () => _httpClient.PostAsync($"{RunPath(executionId)}/{COMPLETE_SEGMENT}", content: null, cancellationToken),
            executionId,
            cancellationToken);

        return response.Then(r => r.Monitor);
    }

    public async Task<ErrorOr<AgentRunMonitor>> GetRunAsync(
        string executionId,
        CancellationToken cancellationToken = default)
    {
        ErrorOr<(AgentRunMonitor Monitor, HttpStatusCode StatusCode)> response = await SendAsync(
            () => _httpClient.GetAsync(RunPath(executionId), cancellationToken),
            executionId,
            cancellationToken);

        return response.Then(r => r.Monitor);
    }

    public async Task<ErrorOr<AgentRunMonitor>> CancelRunAsync(
        string executionId,
        CancellationToken cancellationToken = default)
    {
        ErrorOr<(AgentRunMonitor Monitor, HttpStatusCode StatusCode)> response = await SendAsync(
            () => _httpClient.PostAsync($"{RunPath(executionId)}/{CANCEL_SEGMENT}", content: null, cancellationToken),
            executionId,
            cancellationToken);

        return response.Then(r => r.Monitor);
    }

    public async Task<ErrorOr<AgentRunMonitor>> SubmitInputAsync(
        string executionId,
        string resumeToken,
        string questionId,
        string answer,
        bool allowFurtherPause = true,
        CancellationToken cancellationToken = default)
    {
        var body = new SubmitInputRequestBody(resumeToken, questionId, answer, allowFurtherPause);

        ErrorOr<(AgentRunMonitor Monitor, HttpStatusCode StatusCode)> response = await SendAsync(
            () => _httpClient.PostAsync(
                $"{RunPath(executionId)}/{INPUT_SEGMENT}", MeasuredJson(body), cancellationToken),
            executionId,
            cancellationToken);

        return response.Then(r => r.Monitor);
    }

    /// <summary>
    /// No executionId to validate or echo here, so this stays off the run-scoped
    /// <see cref="SendCoreAsync"/> plumbing; the error taxonomy (failure / invalid response /
    /// unreachable / timeout) mirrors it. A fuse missing from the body is an invalid response,
    /// never a partially-filled view — enforcement built on a partial view would silently skip
    /// the absent fuses.
    /// </summary>
    public Task<ErrorOr<AgentRunLimits>> GetLimitsAsync(CancellationToken cancellationToken = default)
    {
        return GetViewAsync<AgentRunLimits>(
            LIMITS_PATH,
            "limits view",
            "limits request",
            static limits => limits.DeadlineMs is not null
                && limits.MaxBudgetUsd is not null
                && limits.MaxTurns is not null
                && limits.MaxRetainedOutputBytes is not null,
            notFound: null,
            cancellationToken);
    }

    public Task<ErrorOr<AgentRuntimeEnvironment>> GetEnvironmentAsync(CancellationToken cancellationToken = default)
    {
        return GetViewAsync<AgentRuntimeEnvironment>(
            ENVIRONMENT_PATH,
            "environment text",
            "environment request",
            static environment => !string.IsNullOrWhiteSpace(environment.RuntimeVersion)
                && !string.IsNullOrWhiteSpace(environment.Text),
            Error.NotFound(
                AgentRuntimeEnvironmentErrors.NOT_SERVED, "Agent runtime does not serve its environment text"),
            cancellationToken);
    }

    // A GET of one of the runtime's read-only views. A null notFound treats 404 as any other failure.
    private async Task<ErrorOr<TView>> GetViewAsync<TView>(
        string path,
        string viewName,
        string requestName,
        Func<TView, bool> isComplete,
        Error? notFound,
        CancellationToken cancellationToken)
        where TView : class
    {
        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(path, cancellationToken);

            if (notFound is Error notFoundError && response.StatusCode == HttpStatusCode.NotFound)
            {
                return notFoundError;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Agent runtime returned {StatusCode} for its {ViewName}",
                    (int)response.StatusCode,
                    viewName);
                return Error.Failure(
                    ERROR_FAILURE, $"Agent runtime returned {(int)response.StatusCode} for its {viewName}");
            }

            TView? view = ParseBodyOrNull<TView>(await response.Content.ReadAsStringAsync(cancellationToken));

            if (view is null || !isComplete(view))
            {
                _logger.LogError("Agent runtime returned an unparseable {ViewName}", viewName);
                return Error.Failure(ERROR_INVALID_RESPONSE, $"Agent runtime returned an unparseable {viewName}");
            }

            return view;
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "Agent runtime could not be reached for its {ViewName}", viewName);
            return Error.Failure(ERROR_UNREACHABLE, $"Agent runtime could not be reached: {exception.Message}");
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "Agent runtime {RequestName} timed out", requestName);
            return Error.Failure(ERROR_TIMEOUT, $"Agent runtime {requestName} timed out");
        }
    }

    private Task<ErrorOr<(AgentRunMonitor Monitor, HttpStatusCode StatusCode)>> SendAsync(
        Func<Task<HttpResponseMessage>> send,
        string executionId,
        CancellationToken cancellationToken)
    {
        return SendCoreAsync(send, executionId, ReadMonitorSuccessAsync, cancellationToken);
    }

    private async Task<ErrorOr<(AgentRunMonitor Monitor, HttpStatusCode StatusCode)>> ReadMonitorSuccessAsync(
        HttpResponseMessage response,
        string executionId,
        CancellationToken cancellationToken)
    {
        AgentRunMonitor? monitor = await ReadMonitorAsync(response, cancellationToken);
        if (monitor is null || string.IsNullOrWhiteSpace(monitor.ExecutionId))
        {
            _logger.LogError(
                "Agent runtime returned an unparseable monitor for execution {ExecutionId} (status {StatusCode})",
                executionId, (int)response.StatusCode);
            return Error.Failure(ERROR_INVALID_RESPONSE, "Agent runtime returned an unparseable status monitor");
        }

        if (!string.Equals(monitor.ExecutionId, executionId, StringComparison.Ordinal))
        {
            _logger.LogError(
                "Agent runtime returned a monitor for execution {ReturnedExecutionId} while {ExecutionId} was requested",
                monitor.ExecutionId, executionId);
            return Error.Failure(
                ERROR_INVALID_RESPONSE,
                $"Agent runtime returned a monitor for a different run than '{executionId}'");
        }

        return (monitor with { PollInterval = ResolveRetryAfter(response) }, response.StatusCode);
    }

    private async Task<ErrorOr<T>> SendCoreAsync<T>(
        Func<Task<HttpResponseMessage>> send,
        string executionId,
        Func<HttpResponseMessage, string, CancellationToken, Task<ErrorOr<T>>> readSuccess,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!AgentRunIds.IsValid(executionId))
        {
            _logger.LogError("Refused an agent runtime call for illegal execution id {ExecutionId}", executionId);
            return Error.Validation(
                ERROR_VALIDATION,
                "Execution ids must match ^[A-Za-z0-9._-]{1,128}$ — mint them with AgentRunIds.For");
        }

        HttpResponseMessage? response = null;
        try
        {
            response = await send();

            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                return MapError(response, executionId, errorBody);
            }

            return await readSuccess(response, executionId, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "Agent runtime request failed for execution {ExecutionId}", executionId);
            return Error.Failure(ERROR_UNREACHABLE, $"Agent runtime could not be reached: {exception.Message}");
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(exception, "Agent runtime request timed out for execution {ExecutionId}", executionId);
            return Error.Failure(ERROR_TIMEOUT, $"Agent runtime request timed out for run '{executionId}'");
        }
        finally
        {
            response?.Dispose();
        }
    }

    private static async Task<AgentRunMonitor?> ReadMonitorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<AgentRunMonitor>(JsonDefaults.Options, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// One parser for the wire's Retry-After, delta and date forms alike. On a monitor it is
    /// the poll interval — sent on every non-terminal, never on a terminal, so a missing
    /// header maps to null, the stop-polling signal, with no fallback. On a 503 admission
    /// refusal it is the runtime's own wait estimate. Date-form values in the past clamp to
    /// zero.
    /// </summary>
    internal static TimeSpan? ResolveRetryAfter(HttpResponseMessage response)
    {
        RetryConditionHeaderValue? retryAfter = response.Headers.RetryAfter;
        TimeSpan? requested = retryAfter?.Delta ?? retryAfter?.Date - DateTimeOffset.UtcNow;

        if (requested is not TimeSpan interval)
        {
            return null;
        }

        return interval > TimeSpan.Zero ? interval : TimeSpan.Zero;
    }

    private static ReadOnlyMemoryContent MeasuredJson<T>(T body)
    {
        return MeasuredContent(SerializeToBuffer(body));
    }

    /// <summary>
    /// Holds the body in memory so the request declares a Content-Length. The runtime charges its
    /// in-flight upload budget from that declaration <i>before</i> it reads the body, so a
    /// chunked request — which declares nothing — is charged the entire budget and lets exactly
    /// one start through at a time service-wide. The content re-reads its buffer on every send,
    /// which is what lets the resilience handler retry the same request message.
    /// </summary>
    private static ReadOnlyMemoryContent MeasuredContent(ArrayBufferWriter<byte> serialized)
    {
        var content = new ReadOnlyMemoryContent(serialized.WrittenMemory);
        content.Headers.ContentType = new MediaTypeHeaderValue(CONTENT_TYPE_JSON) { CharSet = CHARSET_UTF8 };

        return content;
    }

    private static ArrayBufferWriter<byte> SerializeToBuffer<T>(T body)
    {
        return SerializeToBuffer(sizeHint: 256, body);
    }

    private static ArrayBufferWriter<byte> SerializeToBuffer<T>(int sizeHint, T body)
    {
        var buffer = new ArrayBufferWriter<byte>(sizeHint);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            JsonSerializer.Serialize(writer, body, JsonDefaults.Options);
        }

        return buffer;
    }

    private Error MapError(HttpResponseMessage response, string executionId, string responseBody)
    {
        _logger.LogWarning(
            "Agent runtime returned {StatusCode} for execution {ExecutionId}",
            (int)response.StatusCode, executionId);

        return response.StatusCode switch
        {
            HttpStatusCode.NotFound => Error.NotFound(
                ERROR_NOT_FOUND, $"Agent run '{executionId}' is unknown to the agent runtime"),
            HttpStatusCode.Conflict => BuildConflictError(executionId, responseBody),
            HttpStatusCode.Gone => Error.Conflict(
                ERROR_COMPLETED,
                $"Agent run '{executionId}' was completed — its data is released and its files are gone"),
            HttpStatusCode.BadRequest => Error.Validation(
                ERROR_VALIDATION,
                $"Agent runtime rejected the request for run '{executionId}' as invalid"
                + (string.IsNullOrWhiteSpace(responseBody) ? "" : $": {responseBody.Trim()}")),
            HttpStatusCode.ServiceUnavailable =>
                BuildAdmissionRefusalError(executionId, responseBody, ResolveRetryAfter(response)),
            _ => Error.Failure(
                ERROR_FAILURE, $"Agent runtime returned {(int)response.StatusCode} for run '{executionId}'"),
        };
    }

    /// <summary>
    /// The 503 body discriminates the shutdown drain ({"error":"shutting down"}) from every
    /// other refusal — unknown bodies included — which take the capacity shape. Whatever the
    /// runtime actually said rides into the description, so a reason this client has never
    /// heard of still reaches the operator. The honoured delay rides in metadata for the
    /// caller's durable re-dispatch; nothing waits here (see
    /// <see cref="AgentRunAdmissionErrors"/>).
    /// </summary>
    private Error BuildAdmissionRefusalError(string executionId, string responseBody, TimeSpan? retryAfter)
    {
        if (string.Equals(
            ParseBodyOrNull<AdmissionRefusalBody>(responseBody)?.Reason,
            ADMISSION_REASON_CHAT_DISABLED,
            StringComparison.Ordinal))
        {
            return Error.Failure(
                AgentRunAdmissionErrors.CHAT_DISABLED,
                $"Agent runtime has chat admission switched off and refused run '{executionId}'");
        }

        string? statedReason = DescribeAdmissionReason(responseBody);
        bool shuttingDown = string.Equals(
            statedReason, ADMISSION_BODY_SHUTTING_DOWN, StringComparison.OrdinalIgnoreCase);

        string code = shuttingDown ? AgentRunAdmissionErrors.SHUTTING_DOWN : AgentRunAdmissionErrors.CAPACITY;
        string description = shuttingDown
            ? $"Agent runtime is shutting down and refused run '{executionId}'"
            : $"Agent runtime refused run '{executionId}' for capacity";

        if (!shuttingDown
            && statedReason is not null
            && !string.Equals(statedReason, ADMISSION_BODY_CAPACITY, StringComparison.OrdinalIgnoreCase))
        {
            description += $"; the runtime reported '{statedReason}'";
        }

        // TimeSpan.Zero is the floor ResolveRetryAfter applies to a past or zero header, which on
        // a poll means "poll now" but here would mean "re-post now" — a tight loop against a
        // runtime already refusing. Treat it as no advice and let the caller's backoff decide.
        if (retryAfter is not TimeSpan advertised || advertised <= TimeSpan.Zero)
        {
            return Error.Failure(code, description);
        }

        // A cap that fires silently is a cap nobody can act on: the honoured value below is all
        // the caller ever sees, so an over-cap advertisement is logged as the anomaly it is.
        if (advertised > _retryAfterCap)
        {
            _logger.LogWarning(
                "Agent runtime advertised Retry-After {AdvertisedSeconds}s for execution {ExecutionId}, past the {CapSeconds}s cap",
                advertised.TotalSeconds, executionId, _retryAfterCap.TotalSeconds);
        }

        TimeSpan honoured = HonourRetryAfter(advertised);
        return Error.Failure(
            code,
            // A date-form header resolves to whatever is left of it, which can be under a second;
            // rounding that to "0s" would read as "re-post now" while the metadata says otherwise.
            $"{description}; retry after {honoured.TotalSeconds:0.###}s",
            new Dictionary<string, object>
            {
                [AgentRunAdmissionErrors.RETRY_AFTER_METADATA_KEY] = honoured,
            });
    }

    /// <summary>
    /// The runtime's own words for the refusal: the body's error member when it parses, the
    /// raw body otherwise (an intermediary's error page is still evidence), truncated because
    /// this lands in an error description.
    /// </summary>
    private static string? DescribeAdmissionReason(string responseBody)
    {
        string reason = ParseBodyOrNull<AdmissionRefusalBody>(responseBody)?.Error ?? responseBody;
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        reason = reason.Trim();
        return reason.Length <= ADMISSION_REASON_MAX_CHARS ? reason : reason[..ADMISSION_REASON_MAX_CHARS];
    }

    /// <summary>
    /// The advertised delay wins over any client backoff (RFC 9110 §10.2.3 — the server knows
    /// the pool), but bounded: capped first so a value past the runtime's own ceiling cannot
    /// park a job, then jittered <b>upward only</b> so callers refused together do not return
    /// together. Never downward: Retry-After is a floor, and the runtime derives it from when
    /// a pool slot is expected to free, so returning early only earns another refusal.
    /// </summary>
    private static TimeSpan HonourRetryAfter(TimeSpan advertised)
    {
        TimeSpan capped = advertised < _retryAfterCap ? advertised : _retryAfterCap;
        double jitterFactor = 1 + (Random.Shared.NextDouble() * RETRY_AFTER_JITTER_RATIO);
        return TimeSpan.FromTicks((long)(capped.Ticks * jitterFactor));
    }

    private static Error BuildConflictError(string executionId, string responseBody)
    {
        ConflictResponseBody? body = ParseBodyOrNull<ConflictResponseBody>(responseBody);

        string errorCode = body?.Code switch
        {
            CONFLICT_CODE_PAYLOAD => ERROR_PAYLOAD_CONFLICT,
            CONFLICT_CODE_STALE_RESUME_TOKEN => ERROR_STALE_RESUME_TOKEN,
            CONFLICT_CODE_NOT_AWAITING_INPUT => ERROR_NOT_AWAITING_INPUT,
            CONFLICT_CODE_UNKNOWN_QUESTION => ERROR_UNKNOWN_QUESTION,
            CONFLICT_CODE_NOT_COMPLETABLE => ERROR_NOT_COMPLETABLE,
            _ => ERROR_CONFLICT,
        };

        string description = string.IsNullOrWhiteSpace(body?.Code)
            ? $"Agent runtime reported a conflict for run '{executionId}'"
            : $"Agent runtime reported {body.Code} for run '{executionId}'";

        if (string.IsNullOrWhiteSpace(body?.Status))
        {
            return Error.Conflict(errorCode, description);
        }

        return Error.Conflict(
            errorCode,
            $"{description} (current status: {body.Status})",
            new Dictionary<string, object> { [STATUS_METADATA_KEY] = body.Status });
    }

    private static T? ParseBodyOrNull<T>(string responseBody)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(responseBody, JsonDefaults.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Ids are validated against the sidecar's pattern before any HTTP, so no escaping happens
    /// here — escaping would smuggle illegal ids into a path the sidecar's POST routes read
    /// differently, trading a loud 400 for silent GET/POST disagreement.
    /// </summary>
    private static string RunPath(string executionId)
    {
        return $"{RUNS_PATH}/{executionId}";
    }

    /// <summary>
    /// Pre-flight validation mirroring the runtime's admission checks, so a request the runtime
    /// would 400 or 413 fails here without uploading anything: staged-input names share one
    /// pattern and one namespace across documents and skill files, the skills manifest name is
    /// reserved, and decoded bytes must fit the shared budget.
    /// </summary>
    private static Error? ValidateStartInputs(AgentRunStartRequest request)
    {
        if (request.SystemPrompt is not null && string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            return Error.Validation(
                ERROR_VALIDATION, "SystemPrompt must be non-blank when provided — omit it to use the runtime's default");
        }

        if (request.OutputSchema is not null && request.OutputSchema.ToJsonString().Length > OUTPUT_SCHEMA_MAX_CHARS)
        {
            return Error.Validation(
                ERROR_VALIDATION,
                $"OutputSchema serializes past the runtime's {OUTPUT_SCHEMA_MAX_CHARS} character cap");
        }

        if (request.OutputSchema is not null
            && AgentOutputSchemaDraft07.Validate(request.OutputSchema) is { Count: > 0 } schemaProblems)
        {
            return Error.Validation(
                ERROR_VALIDATION,
                "OutputSchema is not a JSON Schema the runtime's SDK accepts (draft-07): "
                + string.Join("; ", schemaProblems.Select(static problem => $"{problem.Path}: {problem.Message}")));
        }

        if (request.Documents.Count > AgentRunInputCapacity.MAX_DOCUMENTS_PER_RUN)
        {
            return Error.Validation(
                ERROR_VALIDATION,
                $"{request.Documents.Count} documents exceed the runtime's "
                + $"{AgentRunInputCapacity.MAX_DOCUMENTS_PER_RUN}-entry ceiling");
        }

        if (request.SkillFiles.Count > AgentRunInputCapacity.MAX_DOCUMENTS_PER_RUN)
        {
            return Error.Validation(
                ERROR_VALIDATION,
                $"{request.SkillFiles.Count} skill files exceed the runtime's "
                + $"{AgentRunInputCapacity.MAX_DOCUMENTS_PER_RUN}-entry ceiling");
        }

        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (string name in request.Documents
            .Select(static document => document.Name)
            .Concat(request.SkillFiles.Select(static skill => skill.Name)))
        {
            if (!StagedInputNamePattern().IsMatch(name))
            {
                return Error.Validation(
                    ERROR_INVALID_DOCUMENT,
                    $"Input name '{name}' must match ^[A-Za-z0-9._ -]{{1,200}}$");
            }

            if (name is RESERVED_MANIFEST_NAME or "." or "..")
            {
                return Error.Validation(ERROR_INVALID_DOCUMENT, $"Input name '{name}' is reserved");
            }

            if (!seenNames.Add(name))
            {
                return Error.Validation(
                    ERROR_INVALID_DOCUMENT,
                    $"Input name '{name}' is used more than once — documents and skill files share one namespace");
            }
        }

        foreach (AgentRunSkillFile skill in request.SkillFiles)
        {
            if (AgentSkillAssets.DescribeFirstProblem(
                skill.Name,
                skill.Assets.Select(static asset => (asset.Name, asset.Content))) is { } assetProblem)
            {
                return Error.Validation(
                    assetProblem.Kind == AgentSkillAssets.ProblemKind.CountOverCap
                        ? ERROR_VALIDATION
                        : ERROR_INVALID_DOCUMENT,
                    assetProblem.Message);
            }
        }

        if (request.Continuation is AgentRunContinuation continuation
            && ValidateContinuation(continuation) is Error invalidContinuation)
        {
            return invalidContinuation;
        }

        long totalBytes = AgentRunInputCapacity.StartRequestBytes(request);
        if (totalBytes > AgentRunInputCapacity.INPUT_BUDGET_BYTES)
        {
            string subject = request.Continuation is null
                ? "Documents and skill files"
                : "Documents, skill files and re-carried out/ files";

            return Error.Validation(
                ERROR_DOCUMENTS_OVER_BUDGET,
                $"{subject} decode to {totalBytes} bytes, over the runtime's "
                + $"{AgentRunInputCapacity.INPUT_BUDGET_BYTES} byte budget");
        }

        return ValidateSkillIdentities(request.SkillFiles);
    }

    /// <summary>
    /// Mirrors the runtime's continuation admission so a doomed start refuses here
    /// instead of burning the upload: the bundle cap, and the re-carried set's entry cap,
    /// path gate, uniqueness and prefix nesting — one authority for all of it in
    /// <see cref="AgentRunContinuationCapacity"/>. Re-carried paths are their own staged area
    /// on the runtime, so they are deliberately not checked against document or skill names.
    /// </summary>
    private static Error? ValidateContinuation(AgentRunContinuation continuation)
    {
        if (continuation.SessionBundle.Length > AgentRunContinuationCapacity.MAX_SESSION_BUNDLE_BYTES)
        {
            return Error.Validation(
                ERROR_VALIDATION,
                $"The session bundle is {continuation.SessionBundle.Length} bytes, past the runtime's "
                + $"{AgentRunContinuationCapacity.MAX_SESSION_BUNDLE_BYTES}-byte bundle cap");
        }

        if (AgentRunContinuationCapacity.DescribeFirstRecarryProblem(continuation.OutFiles) is string problem)
        {
            return Error.Validation(ERROR_VALIDATION, problem);
        }

        return null;
    }

    private static Error? ValidateSkillIdentities(IReadOnlyList<AgentRunSkillFile> skillFiles)
    {
        return AgentSkillNames.DescribeFirstProblem(
            skillFiles.Select(static skill => (skill.Name, skill.Description ?? string.Empty))) is string problem
            ? Error.Validation(ERROR_VALIDATION, problem)
            : null;
    }

    private Task<ErrorOr<byte[]>> SendRawAsync(
        Func<Task<HttpResponseMessage>> send,
        string executionId,
        CancellationToken cancellationToken)
    {
        return SendCoreAsync(
            send,
            executionId,
            static async (response, _, token) =>
                ErrorOrFactory.From(await response.Content.ReadAsByteArrayAsync(token)),
            cancellationToken);
    }

    /// <summary>
    /// Null members are omitted, not serialized: the runtime strips nulls only on the top-level
    /// record, so an explicit <c>"mediaType": null</c> inside a document item is a 400.
    /// </summary>
    private sealed record StartRunRequestBody(
        string ExecutionId,
        string Prompt,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RunToken,
        IReadOnlyList<DocumentBody>? Documents = null,
        IReadOnlyList<SkillFileBody>? SkillFiles = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Model = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Effort = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SystemPrompt = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] OutputFormatBody? OutputFormat = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? McpServerUrl = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? DeadlineMs = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? MaxBudgetUsd = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? MaxTurns = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? MaxRetainedOutputBytes = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ContinuationBody? Continuation = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AdmissionClass = null);

    private sealed record ContinuationBody(SessionBundleBody SessionBundle, IReadOnlyList<RecarriedFileBody> OutFiles);

    private sealed record SessionBundleBody(
        string Sha256,
        [property: JsonPropertyName("bytesBase64")]
        [property: JsonConverter(typeof(Base64ContentJsonConverter))]
        ReadOnlyMemory<byte> Bytes);

    private sealed record RecarriedFileBody(
        string Path,
        [property: JsonPropertyName("contentBase64")]
        [property: JsonConverter(typeof(Base64ContentJsonConverter))]
        ReadOnlyMemory<byte> Content,
        string Sha256);

    /// <summary>
    /// Schema keys pass through verbatim: <see cref="JsonObject"/> keys are data, not member
    /// names, so the options' camelCase policy never rewrites them.
    /// </summary>
    private sealed record OutputFormatBody(string Type, JsonObject Schema);

    private sealed record DocumentBody(
        string Name,
        [property: JsonPropertyName("contentBase64")]
        [property: JsonConverter(typeof(Base64ContentJsonConverter))]
        ReadOnlyMemory<byte> Content,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? MediaType,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DocumentClass);

    /// <summary>
    /// Streams raw bytes into the wire's base64 string without materializing the intermediate
    /// .NET string — at the 200 MiB document budget that string alone would be ~533 MiB.
    /// </summary>
    private sealed class Base64ContentJsonConverter : JsonConverter<ReadOnlyMemory<byte>>
    {
        public override ReadOnlyMemory<byte> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.GetBytesFromBase64();
        }

        public override void Write(Utf8JsonWriter writer, ReadOnlyMemory<byte> value, JsonSerializerOptions options)
        {
            writer.WriteBase64StringValue(value.Span);
        }
    }

    private sealed record SkillFileBody(
        string Name,
        string ContentBase64,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SkillAssetBody>? Assets = null);

    private sealed record SkillAssetBody(string Name, string ContentBase64);

    /// <summary>
    /// The sidecar's 409 body: {"code": machine-readable conflict code, "status"?: snake_case run
    /// status}. Both stay raw wire strings so unknown future values still surface verbatim.
    /// </summary>
    private sealed record ConflictResponseBody(string? Code, string? Status);

    /// <summary>
    /// The sidecar's 503 body: {"error": "capacity" | "shutting down"}. Raw wire string so
    /// unknown future values still surface verbatim (and fall to the capacity treatment).
    /// </summary>
    private sealed record AdmissionRefusalBody(string? Error, string? Reason = null);

    private sealed record SubmitInputRequestBody(
        string ResumeToken,
        string QuestionId,
        string Answer,
        bool AllowFurtherPause);
}

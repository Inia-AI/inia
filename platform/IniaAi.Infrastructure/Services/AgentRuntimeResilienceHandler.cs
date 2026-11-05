using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using System.Net;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// Transport-level retries for the agent runtime sidecar, safe because every sidecar verb is
/// idempotent by contract: registration replays return the existing monitor, polls are pure
/// reads, cancels and input submissions converge on the same state. Mirrors the repo's
/// Bedrock resilience approach (Polly retry, exponential backoff with jitter).
/// Scope is deliberately narrow — connect failures (<see cref="HttpRequestException"/>) and
/// 5xx responses. Client timeouts are NOT retried here: HttpClient.Timeout cancels the token
/// this handler runs under, so the whole budget is already spent; 4xx/409 are contract
/// answers, not transience.
/// <para>
/// 503 gets its own single attempt rather than the exponential budget, and only when the wait
/// is short enough to hold the request open for. The sidecar refuses with 503 from two
/// unrelated places: an upload-budget shed taken before the request body is even read, which
/// clears in the time one body read takes and advertises a constant 5 s, and a capacity shed on
/// a pool whose runs last minutes. One retry rescues the first; the second surfaces straight
/// away for <see cref="AgentRuntimeClient"/> to type (see <c>AgentRunAdmissionErrors</c>) and
/// for the caller's admission patience to wait out without holding a body. Retrying more would
/// not help either case — each attempt re-uploads the whole request.
/// </para>
/// </summary>
public sealed class AgentRuntimeResilienceHandler : DelegatingHandler
{
    private const int DEFAULT_MAX_RETRY_ATTEMPTS = 3;
    private const int ADMISSION_RETRY_ATTEMPTS = 1;
    private const int MINIMUM_SERVER_ERROR_STATUS_CODE = 500;
    private const double ADMISSION_JITTER_RATIO = 0.25;

    // The upload-budget shed this inline retry exists for advertises a constant 5 s. Anything
    // longer is a saturated pool, whose wait belongs to the caller's admission patience: holding
    // the request here would hold its whole body and outlast the client timeout, turning a typed
    // capacity refusal into an untyped one.
    private static readonly TimeSpan _maxInlineAdmissionWait = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _defaultBaseDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan _defaultAdmissionRetryDelay = TimeSpan.FromSeconds(1);

    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    public AgentRuntimeResilienceHandler(
        ILogger<AgentRuntimeResilienceHandler> logger,
        int maxRetryAttempts = DEFAULT_MAX_RETRY_ATTEMPTS,
        TimeSpan? baseDelay = null,
        TimeSpan? admissionRetryDelay = null)
    {
        TimeSpan baseAdmissionDelay = admissionRetryDelay ?? _defaultAdmissionRetryDelay;

        _pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .HandleResult(static response =>
                        (int)response.StatusCode >= MINIMUM_SERVER_ERROR_STATUS_CODE
                        && response.StatusCode != HttpStatusCode.ServiceUnavailable),
                MaxRetryAttempts = maxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = baseDelay ?? _defaultBaseDelay,
                OnRetry = args =>
                {
                    args.Outcome.Result?.Dispose();
                    logger.LogWarning(
                        args.Outcome.Exception,
                        "Agent runtime transient failure ({Reason}); retry {AttemptNumber} in {DelayMs}ms",
                        args.Outcome.Exception?.GetType().Name ?? ((int?)args.Outcome.Result?.StatusCode)?.ToString(),
                        args.AttemptNumber + 1,
                        args.RetryDelay.TotalMilliseconds);
                    return default;
                },
            })
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .HandleResult(response => response.StatusCode == HttpStatusCode.ServiceUnavailable
                        && IsAffordableInline(AdvisedRetryAfter(response), baseAdmissionDelay)),
                MaxRetryAttempts = ADMISSION_RETRY_ATTEMPTS,
                DelayGenerator = args => new ValueTask<TimeSpan?>(InlineAdmissionRetryDelay(
                    AdvisedRetryAfter(args.Outcome.Result),
                    baseAdmissionDelay,
                    Random.Shared.NextDouble() * ADMISSION_JITTER_RATIO)),
                OnRetry = args =>
                {
                    args.Outcome.Result?.Dispose();
                    logger.LogWarning(
                        "Agent runtime refused with 503; retrying once in {DelayMs}ms in case it was an upload-budget shed",
                        args.RetryDelay.TotalMilliseconds);
                    return default;
                },
            })
            .Build();
    }

    /// <summary>
    /// Retry-After is a floor, not a suggestion (RFC 9110 §10.2.3), so the wait is the greater of
    /// what the runtime advised and this client's own base delay — then jittered upward, never
    /// down, so a fleet refused together does not return in lockstep.
    /// </summary>
    internal static TimeSpan AdmissionRetryDelay(TimeSpan? advised, TimeSpan baseDelay, double jitterFraction)
    {
        TimeSpan floor = advised is TimeSpan value && value > baseDelay ? value : baseDelay;

        return floor + (floor * jitterFraction);
    }

    /// <summary>
    /// The gate admits a 503 on the un-jittered wait, so the jitter is capped at the same bound
    /// rather than allowed to hold the request — and its whole body — past what the gate judged
    /// affordable. Only a wait already at the bound loses its jitter.
    /// </summary>
    internal static TimeSpan InlineAdmissionRetryDelay(TimeSpan? advised, TimeSpan baseDelay, double jitterFraction)
    {
        TimeSpan jittered = AdmissionRetryDelay(advised, baseDelay, jitterFraction);

        return jittered < _maxInlineAdmissionWait ? jittered : _maxInlineAdmissionWait;
    }

    private static bool IsAffordableInline(TimeSpan? advised, TimeSpan baseDelay)
    {
        return AdmissionRetryDelay(advised, baseDelay, jitterFraction: 0) <= _maxInlineAdmissionWait;
    }

    private static TimeSpan? AdvisedRetryAfter(HttpResponseMessage? response)
    {
        return response is not null
            && AgentRuntimeClient.ResolveRetryAfter(response) is TimeSpan advised
            && advised > TimeSpan.Zero
                ? advised
                : null;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async token => await base.SendAsync(request, token).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }
}

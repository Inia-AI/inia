using IniaAi.Common;
using IniaAi.Common.Constants;
using IniaAi.Common.Http;
using IniaAi.Common.Text;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Workflows.Interfaces;
using IniaAi.Core.Workflows.Strategies.ParallelAi;
using IniaAi.Core.Workflows.Strategies.Perplexity;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.Domain.StoredKeys;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace IniaAi.Core.Workflows.Strategies;

public partial class WebApiCallStrategy : IBlockStrategy
{
    private const int TIMEOUT_SECONDS = 120;
    private const int MAX_RETRIES = 5;
    private const int MAX_RETRY_AFTER_SECONDS = 120;
    private const int TOO_EARLY_STATUS_CODE = 425;
    private const string STATUS_CODE_DATA_KEY = "StatusCode";
    private const string RETRY_AFTER_DATA_KEY = "RetryAfter";
    private const string RETRY_AFTER_SOURCE = "Retry-After header";
    private const string STORED_KEYS_UNAVAILABLE =
        "This run cannot read stored keys, so the block cannot fill in its stored-key placeholders.";
    private const string STORED_KEY_MISSING =
        "This organization has no stored key named '{0}'. An admin adds it under Administration, Stored keys.";
    private const string BACKOFF_SCHEDULE_SOURCE = "backoff schedule";
    private const string PERPLEXITY_HOST = WebApiCallSettings.PERPLEXITY_HOST;
    private const string PERPLEXITY_CHAT_COMPLETIONS_PATH = "/chat/completions";
    private const string PARALLEL_AI_HOST = "api.parallel.ai";
    private const int MIN_POLL_INTERVAL_SECONDS = 5;
    private const int DEFAULT_POLL_INTERVAL_SECONDS = 15;
    private const int DEFAULT_POLL_TIMEOUT_SECONDS = 600;
    private const int MAX_POLL_TIMEOUT_SECONDS = 3600;
    private const int MAX_CONSECUTIVE_POLL_ERRORS = 5;
    private const int MILLISECONDS_PER_SECOND = 1000;
    private static readonly int[] _retryDelaysMs = [1000, 2000, 4000, 8000, 16000];

    // Client errors that will not heal by polling, so polling fails fast instead of
    // waiting out the timeout. Other 4xx, all 5xx, timeouts and network errors are
    // treated as transient and tolerated until the deadline.
    private static readonly HashSet<HttpStatusCode> _pollFailFastStatusCodes =
        [HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden];

    // 4xx statuses that mean "try again shortly" rather than "the request is wrong". 408 and
    // 425 ride the same fixed schedule as 5xx and network errors; 429 has its own longer,
    // jittered schedule because it is the server saying the caller is going too fast.
    private static readonly HashSet<HttpStatusCode> _transientClientStatusCodes =
        [HttpStatusCode.RequestTimeout, (HttpStatusCode)TOO_EARLY_STATUS_CODE];

    private readonly TimeProvider _timeProvider;

    public WebApiCallStrategy(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected virtual TimeSpan RequestTimeout => TimeSpan.FromSeconds(TIMEOUT_SECONDS);

    protected virtual IReadOnlyList<int> RetryDelaysMs => _retryDelaysMs;

    /// <summary>
    /// The 429 schedule: how many retries, the jitter window per retry and the total backoff
    /// budget. Overridable so tests can shrink it without touching the transient schedule.
    /// </summary>
    protected virtual WebApiCallRateLimitSchedule RateLimitSchedule => WebApiCallRateLimitSchedule.Default;

    /// <summary>
    /// The per-host concurrency gate the send goes through. Process-wide by default; a test
    /// substitutes its own instance so its limit cannot leak into other tests on the same host.
    /// </summary>
    protected virtual WebApiCallHostGate HostGate => WebApiCallHostGate.Shared;

    /// <summary>
    /// The limits that size the gate. Read from the deployment's <see cref="PlatformSettings"/>
    /// the same way every other strategy setting reaches a block.
    /// </summary>
    protected virtual WebApiCallSettings ResolveSettings(BlockExecutionContext context)
    {
        return context.PlatformSettings.WebApiCall;
    }

    /// <summary>
    /// The wait before a retried request. Extracted so tests can record the delay instead of
    /// sleeping it.
    /// </summary>
    protected virtual Task DelayBeforeRetryAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return Task.Delay(delay, cancellationToken);
    }

    [GeneratedRegex(@"^/v1/tasks/runs/[^/]+/result/?$", RegexOptions.IgnoreCase)]
    private static partial Regex ParallelAiTaskResultPathRegex();

    public async Task<BlockStrategyOutput> ExecuteBlockAsync(
        Block block,
        BlockExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        BlockStrategyOutput output = new();

        WebApiCallProperties props = block.Properties as WebApiCallProperties
            ?? throw new InvalidOperationException($"Block properties must be of type {nameof(WebApiCallProperties)}");

        props.ExecutionState ??= new (props);

        // Validate inputs
        ValidateInputs(block, props, context);

        string url = props.ExecutionState.Url
            ?? throw new InvalidOperationException("URL is required");

        string method = props.ExecutionState.HttpMethod
            ?? "GET";

        string? payload = method.Equals("POST", StringComparison.InvariantCultureIgnoreCase)
            ? props.ExecutionState.RequestPayload
            : null;

        Dictionary<string, string> headers = await WithStoredKeysAsync(
            props.ExecutionState.AdditionalHeaders,
            context,
            cancellationToken);

        string contentType = props.ExecutionState.ContentType
            ?? "application/json";

        string acceptHeader = props.ExecutionState.AcceptHeader
            ?? "application/json";

        context.Logger.LogDebug(
            "{block} Making {method} request to {url}",
            block,
            method,
            LogSanitizer.ForLogUrl(url));

        context.Logger.LogTrace(
            "{block} Request payload: {Length} characters",
            block,
            payload?.Length ?? 0);

        context.Logger.LogTrace(
            "{block} Request headers: {headers}",
            block,
            string.Join(", ", headers.Select(h => $"{h.Key}:{SecretMasker.Mask(h.Value)}")));

        foreach (string warning in props.ExecutionState.SanitizationWarnings)
        {
            context.Logger.LogWarning("{block} {SanitizationWarning}", block, warning);
        }

        using (var headerProbe = new HttpRequestMessage())
        {
            PopulateHeaders(headerProbe.Headers, acceptHeader, headers, (headerName, reason) =>
                context.Logger.LogWarning(
                    "{block} Header '{header}' is invalid and will be skipped: {reason}",
                    block,
                    headerName,
                    reason));
        }

        string response;
        string responseUrl = url;

        // Poll-until-condition mode is opt-in: enabled only when a status path is set.
        // It supersedes the fixed DelayAfterRequestSeconds (which is ignored here).
        if (!string.IsNullOrWhiteSpace(props.PollUntilJsonPath))
        {
            response = await PollUntilConditionAsync(
                props,
                url,
                method,
                payload,
                headers,
                contentType,
                acceptHeader,
                context,
                block,
                cancellationToken);

            // Optional completion action: fetch a second URL once polling succeeds and
            // use that as the block output (e.g. parallel.ai /result after the status poll).
            string? completionUrl = props.ExecutionState.PollCompletionUrl;

            if (!string.IsNullOrWhiteSpace(completionUrl))
            {
                context.Logger.LogDebug(
                    "{block} polling complete, fetching completion URL {url}",
                    block,
                    LogSanitizer.ForLogUrl(completionUrl));

                response = await ExecuteSingleRequestAsync(
                    completionUrl,
                    "GET",
                    null,
                    headers,
                    contentType,
                    acceptHeader,
                    context,
                    block,
                    cancellationToken);

                responseUrl = completionUrl;

                context.Logger.LogTrace(
                    "{block} Completion response: {Length} characters",
                    block,
                    response.Length);
            }
        }
        else
        {
            response = await ExecuteSingleRequestAsync(
                url,
                method,
                payload,
                headers,
                contentType,
                acceptHeader,
                context,
                block,
                cancellationToken);

            context.Logger.LogTrace(
                "{block} Response: {Length} characters",
                block,
                response.Length);

            // Apply delay if configured (for async API experimentation)
            int delaySeconds = props.DelayAfterRequestSeconds ?? 0;

            if (delaySeconds > 0)
            {
                context.Logger.LogDebug(
                    "{block} Waiting {delaySeconds} seconds for async operation",
                    block,
                    delaySeconds);

                await Task.Delay(delaySeconds * MILLISECONDS_PER_SECOND, cancellationToken);
            }
        }

        bool isPerplexity = IsPerplexityChatCompletionsUrl(responseUrl);
        bool isParallelAi = IsParallelAiTaskResultUrl(responseUrl);

        if (isPerplexity
            && PerplexityResponseParser.TryParse(response, out PerplexityParseResult perplexityParsed))
        {
            block.BlockResponse = new BlockResponse(block)
            {
                RawResponse = perplexityParsed.CleanedBody,
                JsonResponse = response,
            };
            if (context.IsCitationSource)
            {
                context.BlockAnnotations.AddFacts(perplexityParsed.Facts);
            }
        }
        else if (isParallelAi
            && ParallelAiResponseParser.TryParse(response, out ParallelAiParseResult parallelAiParsed))
        {
            block.BlockResponse = new BlockResponse(block)
            {
                RawResponse = parallelAiParsed.CleanedBody,
                JsonResponse = response,
            };
            if (context.IsCitationSource)
            {
                context.BlockAnnotations.AddFacts(parallelAiParsed.Facts);
            }
        }
        else
        {
            if (isPerplexity)
            {
                context.Logger.LogWarning(
                    "{block} URL {url} matches Perplexity but response did not contain extractable citations; falling back to raw response",
                    block,
                    LogSanitizer.ForLogUrl(responseUrl));
            }
            else if (isParallelAi)
            {
                context.Logger.LogWarning(
                    "{block} URL {url} matches Parallel.ai task result but response did not contain extractable citations; falling back to raw response",
                    block,
                    LogSanitizer.ForLogUrl(responseUrl));
            }

            block.BlockResponse = new BlockResponse(block)
            {
                RawResponse = response,
                JsonResponse = response,
            };
        }

        return output;
    }

    /// <summary>
    /// Repeatedly calls the (resolved) URL at a fixed interval, evaluating the JSON
    /// status field after each call: returns the response on a success value, throws
    /// on a failure value (fail fast), and throws a <see cref="TimeoutException"/>
    /// when the deadline is reached.
    /// </summary>
    private async Task<string> PollUntilConditionAsync(
        WebApiCallProperties props,
        string url,
        string method,
        string? payload,
        Dictionary<string, string> headers,
        string contentType,
        string acceptHeader,
        BlockExecutionContext context,
        Block block,
        CancellationToken cancellationToken)
    {
        string jsonPath = props.PollUntilJsonPath!;

        HashSet<string> successValues = ParsePollValues(props.PollUntilSuccessValues);

        if (successValues.Count == 0)
        {
            string errorMessage =
                $"Polling is enabled ({nameof(WebApiCallProperties.PollUntilJsonPath)} is set) "
                + $"but {nameof(WebApiCallProperties.PollUntilSuccessValues)} is empty; "
                + "at least one success value is required.";
            context.Logger.LogError("{block} {ErrorMessage}", block, errorMessage);
            throw new InvalidOperationException(errorMessage);
        }

        HashSet<string> failureValues = ParsePollValues(props.PollUntilFailureValues);

        if (successValues.Overlaps(failureValues))
        {
            string overlapMessage =
                $"{nameof(WebApiCallProperties.PollUntilSuccessValues)} and "
                + $"{nameof(WebApiCallProperties.PollUntilFailureValues)} share one or more values; "
                + "a status cannot mean both success and failure.";
            context.Logger.LogError("{block} {ErrorMessage}", block, overlapMessage);
            throw new InvalidOperationException(overlapMessage);
        }

        int interval = Math.Max(
            props.PollIntervalSeconds ?? DEFAULT_POLL_INTERVAL_SECONDS,
            MIN_POLL_INTERVAL_SECONDS);

        int timeout = Math.Clamp(
            props.PollTimeoutSeconds ?? DEFAULT_POLL_TIMEOUT_SECONDS,
            interval,
            MAX_POLL_TIMEOUT_SECONDS);

        if (props.PollIntervalSeconds is int requestedInterval && requestedInterval != interval)
        {
            context.Logger.LogWarning(
                "{block} polling: requested interval {requested}s clamped to {effective}s (minimum {min}s)",
                block,
                requestedInterval,
                interval,
                MIN_POLL_INTERVAL_SECONDS);
        }

        if (props.PollTimeoutSeconds is int requestedTimeout && requestedTimeout != timeout)
        {
            context.Logger.LogWarning(
                "{block} polling: requested timeout {requested}s clamped to {effective}s (allowed {min}s..{max}s)",
                block,
                requestedTimeout,
                timeout,
                interval,
                MAX_POLL_TIMEOUT_SECONDS);
        }

        DateTimeOffset deadline = _timeProvider.GetUtcNow() + TimeSpan.FromSeconds(timeout);
        int attempt = 0;
        int consecutiveErrors = 0;
        string? lastStatus = null;
        string? lastError = null;
        bool warnedUnresolvedPath = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_timeProvider.GetUtcNow() >= deadline)
            {
                throw PollingTimeout(block, timeout, attempt, lastStatus, lastError);
            }

            attempt++;

            // Each poll is a single attempt bounded by the time left until the deadline;
            // the loop itself is the retry mechanism. The deadline timer rides on the
            // injected TimeProvider so it stays inert under a faked clock in tests.
            string? response = null;
            bool requestErrored = false;
            TimeSpan remaining = deadline - _timeProvider.GetUtcNow();

            using (var deadlineCts = new CancellationTokenSource(remaining, _timeProvider))
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                deadlineCts.Token))
            {
                try
                {
                    response = (await ExecuteGatedRequestAsync(
                        url,
                        method,
                        payload,
                        headers,
                        contentType,
                        acceptHeader,
                        context,
                        linkedCts.Token)).RemoveNullCharacters();
                }
                catch (OperationCanceledException)
                    when (deadlineCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    throw PollingTimeout(block, timeout, attempt, lastStatus, lastError);
                }
                catch (Exception ex) when (IsTransientPollError(ex))
                {
                    requestErrored = true;
                    lastError = ex.Message;
                }
            }

            if (requestErrored)
            {
                consecutiveErrors++;
                context.Logger.LogWarning(
                    "{block} polling: request error on attempt {attempt} ({error}); {count}/{max} consecutive",
                    block,
                    attempt,
                    lastError,
                    consecutiveErrors,
                    MAX_CONSECUTIVE_POLL_ERRORS);

                if (consecutiveErrors >= MAX_CONSECUTIVE_POLL_ERRORS)
                {
                    throw new InvalidOperationException(
                        $"{block} polling: {consecutiveErrors} consecutive request errors; last error = {lastError}");
                }
            }
            else
            {
                consecutiveErrors = 0;
                lastStatus = ExtractJsonField(response!, jsonPath);

                if (lastStatus is null && !warnedUnresolvedPath)
                {
                    warnedUnresolvedPath = true;
                    context.Logger.LogInformation(
                        "{block} polling: status path '{path}' did not resolve to a primitive value; "
                        + "will keep polling until success or timeout",
                        block,
                        jsonPath);
                }

                if (lastStatus is not null && successValues.Contains(lastStatus))
                {
                    context.Logger.LogDebug(
                        "{block} polling: success status '{status}' after {attempt} attempts",
                        block,
                        lastStatus,
                        attempt);
                    return response!;
                }

                if (lastStatus is not null && failureValues.Contains(lastStatus))
                {
                    string failureMessage =
                        $"{block} polling: task reported failure status '{lastStatus}' after {attempt} attempts";
                    context.Logger.LogError("{block} {ErrorMessage}", block, failureMessage);
                    throw new InvalidOperationException(failureMessage);
                }
            }

            TimeSpan remainingBeforeDelay = deadline - _timeProvider.GetUtcNow();

            if (remainingBeforeDelay <= TimeSpan.Zero)
            {
                throw PollingTimeout(block, timeout, attempt, lastStatus, lastError);
            }

            // Cap the wait at the time remaining so polling cannot exceed PollTimeoutSeconds.
            int delayMs = (int)Math.Min(
                (long)interval * MILLISECONDS_PER_SECOND,
                (long)remainingBeforeDelay.TotalMilliseconds);

            context.Logger.LogTrace(
                "{block} polling: poll #{attempt}: status='{status}', next poll in {delaySeconds:0.###}s",
                block,
                attempt,
                requestErrored ? "(error)" : (lastStatus ?? "(none)"),
                delayMs / (double)MILLISECONDS_PER_SECOND);

            await DelayBetweenPollsAsync(delayMs, cancellationToken);
        }
    }

    private static TimeoutException PollingTimeout(
        Block block,
        int timeout,
        int attempt,
        string? lastStatus,
        string? lastError)
    {
        string message = $"{block} polling timed out after {timeout}s ({attempt} attempts); "
            + $"last status = '{lastStatus ?? "(none)"}'";

        if (lastError is not null)
        {
            message += $"; last error = {lastError}";
        }

        return new TimeoutException(message);
    }

    private static bool IsTransientPollError(Exception ex)
    {
        if (ex is HttpRequestException httpEx)
        {
            return !(httpEx.Data[STATUS_CODE_DATA_KEY] is HttpStatusCode code && _pollFailFastStatusCodes.Contains(code));
        }

        // HttpClient's own per-request timeout surfaces as a cancelled task wrapping a TimeoutException.
        return ex is TaskCanceledException { InnerException: TimeoutException };
    }

    /// <summary>
    /// The inter-poll wait. Extracted so tests can override it to avoid real waiting.
    /// </summary>
    protected virtual Task DelayBetweenPollsAsync(int milliseconds, CancellationToken cancellationToken)
    {
        return Task.Delay(milliseconds, cancellationToken);
    }

    /// <summary>
    /// Parses a comma-separated list into a case-insensitive set, ignoring blanks.
    /// </summary>
    private static HashSet<string> ParsePollValues(string? commaSeparated)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(commaSeparated))
        {
            return values;
        }

        foreach (string value in commaSeparated.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            values.Add(value);
        }

        return values;
    }

    /// <summary>
    /// Walks a dot-path (e.g. <c>run.status</c>) into the JSON body and returns the
    /// leaf as a string. Primitive leaves (string/number/boolean) are stringified;
    /// a missing path, an object/array leaf, or a non-JSON body returns null so the
    /// caller treats it as "condition not met yet".
    /// </summary>
    private static string? ExtractJsonField(string body, string dotPath)
    {
        if (string.IsNullOrWhiteSpace(body) || string.IsNullOrWhiteSpace(dotPath))
        {
            return null;
        }

        JToken root;
        try
        {
            root = JToken.Parse(body);
        }
        catch (JsonReaderException)
        {
            return null;
        }

        JToken? current = root;
        foreach (string segment in dotPath.Split(
            '.',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current is not JObject obj)
            {
                return null;
            }

            current = obj[segment];

            if (current is null)
            {
                return null;
            }
        }

        return current.Type switch
        {
            JTokenType.String => current.Value<string>(),
            JTokenType.Integer or JTokenType.Float =>
                Convert.ToString(((JValue)current).Value, CultureInfo.InvariantCulture),
            JTokenType.Boolean => current.Value<bool>() ? "true" : "false",
            _ => null,
        };
    }

    /// <summary>
    /// Executes a single request through the transient-retry path, rewrapping the
    /// HttpClient per-request timeout as a plain <see cref="TimeoutException"/>.
    /// </summary>
    private async Task<string> ExecuteSingleRequestAsync(
        string url,
        string method,
        string? payload,
        Dictionary<string, string> headers,
        string contentType,
        string acceptHeader,
        BlockExecutionContext context,
        Block block,
        CancellationToken cancellationToken)
    {
        using var requestCancellationTokenSource = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);

        try
        {
            string response = await ExecuteRequestWithRetryAsync(
                url,
                method,
                payload,
                headers,
                contentType,
                acceptHeader,
                context,
                block,
                requestCancellationTokenSource.Token);

            return response.RemoveNullCharacters();
        }
        // HttpClient signals its internal per-request timeout via TaskCanceledException
        // wrapping a TimeoutException. That type derives from OperationCanceledException,
        // but it is NOT a workflow-level cancellation. Rewrap as a plain TimeoutException
        // so the surrounding block-loop handles it as a regular block failure instead of
        // tripping the JobExecutor cancellation arm and stranding the WorkflowExecution.
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            string message = $"WebApiCall request to {HostOf(url)} timed out after {TIMEOUT_SECONDS}s";
            context.Logger.LogWarning(ex, "{block} {message}", block, message);
            throw new TimeoutException(message, ex);
        }
    }

    private static bool IsPerplexityChatCompletionsUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            && uri.Host.Equals(PERPLEXITY_HOST, StringComparison.OrdinalIgnoreCase)
            && TrimSingleTrailingSlash(uri.AbsolutePath)
                .Equals(PERPLEXITY_CHAT_COMPLETIONS_PATH, StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimSingleTrailingSlash(string path)
    {
        return path.Length > 1 && path[^1] == '/'
            ? path[..^1]
            : path;
    }

    private static bool IsParallelAiTaskResultUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            && uri.Host.Equals(PARALLEL_AI_HOST, StringComparison.OrdinalIgnoreCase)
            && ParallelAiTaskResultPathRegex().IsMatch(uri.AbsolutePath);
    }

    /// <summary>
    /// The request headers with each stored-key placeholder filled in. The block's execution state
    /// keeps the placeholders, so the stored value reaches only the request.
    /// </summary>
    private static async Task<Dictionary<string, string>> WithStoredKeysAsync(
        Dictionary<string, string> headers,
        BlockExecutionContext context,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string>? requestHeaders = null;

        foreach ((string headerName, string headerValue) in headers)
        {
            string value = headerValue;
            bool filled = false;

            foreach (string placeholder in headerValue.GetPlaceholders())
            {
                if (StoredKeyPlaceholders.KeyName(placeholder) is not string storedKeyName)
                {
                    continue;
                }

                if (context.StoredKeys is null || string.IsNullOrEmpty(context.OrganizationId))
                {
                    throw new InvalidOperationException(STORED_KEYS_UNAVAILABLE);
                }

                string storedValue = await context.StoredKeys.ResolveAsync(context.OrganizationId, storedKeyName, cancellationToken)
                    ?? throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, STORED_KEY_MISSING, storedKeyName));

                value = value.ReplacePlaceholders(asPlaceHolder: true, new() { [placeholder] = storedValue }, ReplacementTags.Comparison)!;
                filled = true;
            }

            if (filled)
            {
                requestHeaders ??= new Dictionary<string, string>(headers, headers.Comparer);
                requestHeaders[headerName] = value;
            }
        }

        return requestHeaders ?? headers;
    }

    private static void ValidateInputs(
        Block block,
        WebApiCallProperties props,
        BlockExecutionContext context)
    {
        props.ExecutionState ??= new (props);

        if (string.IsNullOrWhiteSpace(props.ExecutionState.Url))
        {
            string errorMessage = "URL is required";
            context.Logger.LogError("{block} {ErrorMessage}", block, errorMessage);
            throw new InvalidOperationException(errorMessage);
        }

        if (!Uri.TryCreate(props.ExecutionState.Url, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            string errorMessage = $"Invalid URL format: {LogSanitizer.ForLogUrl(props.ExecutionState.Url)}";
            context.Logger.LogError("{block} {ErrorMessage}", block, errorMessage);
            throw new InvalidOperationException(errorMessage);
        }

        string method = props.ExecutionState.HttpMethod
            ?? "GET";
        method = method.ToUpperInvariant();

        if (method is not "GET" and not "POST")
        {
            string errorMessage = $"Unsupported HTTP method: {method}. Only GET and POST are supported.";
            context.Logger.LogError("{block} {ErrorMessage}", block, errorMessage);
            throw new InvalidOperationException(errorMessage);
        }

        string? payload = props.ExecutionState.RequestPayload;
        string contentType = props.ExecutionState.ContentType ?? "application/json";

        if (method == "POST"
            && !contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
            && HasEmbeddedPlaceholders(props.RequestPayloadTemplate))
        {
            string errorMessage =
                $"Request payload placeholders are JSON-escaped when substituted, which would corrupt a non-JSON body (Content-Type '{contentType}'). "
                + "Use a JSON content type, or make the whole payload a single placeholder — single placeholders substitute verbatim.";
            context.Logger.LogError("{block} {ErrorMessage}", block, errorMessage);
            throw new InvalidOperationException(errorMessage);
        }

        if (method == "POST"
            && contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(payload))
        {
            try
            {
                _ = System.Text.Json.JsonElement.Parse(payload);
            }
            catch (System.Text.Json.JsonException ex)
            {
                string errorMessage =
                    $"Request payload is not valid JSON after placeholder substitution: {ex.Message}";
                context.Logger.LogError("{block} {ErrorMessage}", block, errorMessage);
                throw new InvalidOperationException(errorMessage, ex);
            }
        }

        string? completionUrl = props.ExecutionState.PollCompletionUrl;

        if (!string.IsNullOrWhiteSpace(completionUrl))
        {
            if (string.IsNullOrWhiteSpace(props.PollUntilJsonPath))
            {
                string errorMessage =
                    $"{nameof(WebApiCallProperties.PollCompletionUrlTemplate)} is set but polling is not enabled; "
                    + $"set {nameof(WebApiCallProperties.PollUntilJsonPath)} to enable polling.";
                context.Logger.LogError("{block} {ErrorMessage}", block, errorMessage);
                throw new InvalidOperationException(errorMessage);
            }

            if (!Uri.TryCreate(completionUrl, UriKind.Absolute, out Uri? completionUri)
                || (completionUri.Scheme != "http" && completionUri.Scheme != "https"))
            {
                string errorMessage = $"Invalid completion URL format: {completionUrl}";
                context.Logger.LogError("{block} {ErrorMessage}", block, errorMessage);
                throw new InvalidOperationException(errorMessage);
            }
        }
    }

    /// <summary>
    /// Sends the request until it succeeds or a retry class runs out. Two classes are tracked
    /// separately: transient failures (network errors, request timeouts, 5xx, 408, 425) on the
    /// fixed <see cref="RetryDelaysMs"/> schedule, and 429 on <see cref="RateLimitSchedule"/>,
    /// which honours <c>Retry-After</c> and is bounded by a total backoff budget. Every wait is
    /// logged at Warning; exhaustion throws with the final status, attempts and time waited.
    /// </summary>
    private async Task<string> ExecuteRequestWithRetryAsync(
        string url,
        string method,
        string? payload,
        Dictionary<string, string> headers,
        string contentType,
        string acceptHeader,
        BlockExecutionContext context,
        Block block,
        CancellationToken cancellationToken)
    {
        WebApiCallRateLimitSchedule schedule = RateLimitSchedule;
        int attempts = 0;
        int transientRetries = 0;
        int rateLimitRetries = 0;
        long totalWaitedMs = 0;

        while (true)
        {
            attempts++;

            try
            {
                return await ExecuteGatedRequestAsync(
                    url,
                    method,
                    payload,
                    headers,
                    contentType,
                    acceptHeader,
                    context,
                    cancellationToken);
            }
            catch (HttpRequestException ex) when (IsRateLimited(ex))
            {
                if (rateLimitRetries >= schedule.MaxRetries)
                {
                    throw RetriesExhausted(
                        url,
                        ex,
                        attempts,
                        totalWaitedMs,
                        $"the {schedule.MaxRetries} rate-limit retries are used up");
                }

                long remainingMs = schedule.TotalWaitCeilingMs - totalWaitedMs;
                int delayMs;
                string delaySource;

                if (ex.Data[RETRY_AFTER_DATA_KEY] is TimeSpan retryAfter)
                {
                    delayMs = (int)Math.Ceiling(retryAfter.TotalMilliseconds);
                    delaySource = RETRY_AFTER_SOURCE;

                    if (delayMs > remainingMs)
                    {
                        throw RetriesExhausted(
                            url,
                            ex,
                            attempts,
                            totalWaitedMs,
                            $"Retry-After asks for {FormatSeconds(delayMs)}s but only {FormatSeconds(remainingMs)}s "
                            + $"of the {FormatSeconds(schedule.TotalWaitCeilingMs)}s backoff budget remain");
                    }
                }
                else
                {
                    if (remainingMs <= 0)
                    {
                        throw RetriesExhausted(
                            url,
                            ex,
                            attempts,
                            totalWaitedMs,
                            $"the {FormatSeconds(schedule.TotalWaitCeilingMs)}s backoff budget is used up");
                    }

                    delayMs = (int)Math.Min(schedule.NextJitteredDelayMs(rateLimitRetries), remainingMs);
                    delaySource = BACKOFF_SCHEDULE_SOURCE;
                }

                LogRetryWait(context, block, ex, attempts, delayMs, delaySource);
                await DelayBeforeRetryAsync(TimeSpan.FromMilliseconds(delayMs), cancellationToken);
                totalWaitedMs += delayMs;
                rateLimitRetries++;
            }
            catch (HttpRequestException ex) when (IsTransient(ex))
            {
                if (transientRetries >= MAX_RETRIES)
                {
                    throw RetriesExhausted(
                        url,
                        ex,
                        attempts,
                        totalWaitedMs,
                        $"the {MAX_RETRIES} transient-failure retries are used up");
                }

                int delayMs = RetryDelaysMs[transientRetries];
                LogRetryWait(context, block, ex, attempts, delayMs, BACKOFF_SCHEDULE_SOURCE);
                await DelayBeforeRetryAsync(TimeSpan.FromMilliseconds(delayMs), cancellationToken);
                totalWaitedMs += delayMs;
                transientRetries++;
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                if (transientRetries >= MAX_RETRIES)
                {
                    throw new TimeoutException(
                        $"WebApiCall to {HostOf(url)} timed out after {TIMEOUT_SECONDS}s on attempt {attempts} "
                        + $"with {FormatSeconds(totalWaitedMs)}s of backoff: the {MAX_RETRIES} transient-failure retries are used up",
                        ex);
                }

                int delayMs = RetryDelaysMs[transientRetries];
                context.Logger.LogWarning(
                    "{block} Request timeout (attempt {attempt}/{maxAttempts}), retrying in {delay}ms",
                    block,
                    attempts,
                    MAX_RETRIES + 1,
                    delayMs);

                await DelayBeforeRetryAsync(TimeSpan.FromMilliseconds(delayMs), cancellationToken);
                totalWaitedMs += delayMs;
                transientRetries++;
            }
        }
    }

    /// <summary>
    /// One send through the per-host concurrency gate. The slot is held only for the round
    /// trip, never across a backoff wait, and is released in <c>finally</c> whatever the outcome.
    /// </summary>
    private async Task<string> ExecuteGatedRequestAsync(
        string url,
        string method,
        string? payload,
        Dictionary<string, string> headers,
        string contentType,
        string acceptHeader,
        BlockExecutionContext context,
        CancellationToken cancellationToken)
    {
        string host = new Uri(url, UriKind.Absolute).Host;
        WebApiCallSettings settings = ResolveSettings(context);
        string gateKey = WebApiCallHostGate.BuildKey(host, headers);
        int limit = settings.ResolveMaxConcurrentRequests(host);

        using IDisposable slot = await HostGate.AcquireAsync(gateKey, limit, cancellationToken);

        try
        {
            return await ExecuteRequestAsync(
                context.PlatformSettings.OutboundAccess.Guard.SharedHandlerWith(context.WebApiCookies),
                url,
                method,
                payload,
                headers,
                contentType,
                acceptHeader,
                cancellationToken);
        }
        catch (OutboundAddressRefusedException refusal)
        {
            context.Logger.LogWarning(
                "A WebApiCall request was refused ({Address}): {Reason}",
                refusal.Address,
                LogSanitizer.ForLog(refusal.Message));
            throw;
        }
    }

    private static bool IsRateLimited(HttpRequestException ex)
    {
        return ex.Data[STATUS_CODE_DATA_KEY] is HttpStatusCode.TooManyRequests;
    }

    private static bool IsTransient(HttpRequestException ex)
    {
        if (ex.Data[STATUS_CODE_DATA_KEY] is HttpStatusCode statusCode)
        {
            return (int)statusCode >= (int)HttpStatusCode.InternalServerError
                || _transientClientStatusCodes.Contains(statusCode);
        }

        // No status at all: the failure happened below HTTP (DNS, connection reset, TLS).
        return true;
    }

    private static void LogRetryWait(
        BlockExecutionContext context,
        Block block,
        HttpRequestException ex,
        int attempt,
        int delayMs,
        string delaySource)
    {
        context.Logger.LogWarning(
            "{block} HTTP request failed with {status} (attempt {attempt}), retrying in {delay}ms per {source}",
            block,
            ex.Message,
            attempt,
            delayMs,
            delaySource);
    }

    private static HttpRequestException RetriesExhausted(
        string url,
        HttpRequestException lastFailure,
        int attempts,
        long totalWaitedMs,
        string reason)
    {
        string attemptNoun = attempts == 1 ? "attempt" : "attempts";
        var exhausted = new HttpRequestException(
            $"WebApiCall to {HostOf(url)} failed with {lastFailure.Message} after {attempts} {attemptNoun} "
            + $"and {FormatSeconds(totalWaitedMs)}s of backoff: {reason}",
            lastFailure);

        foreach (object key in lastFailure.Data.Keys)
        {
            exhausted.Data[key] = lastFailure.Data[key];
        }

        return exhausted;
    }

    // Messages name the host only: a URL can carry a credential in its query string, and these
    // messages land in the block log and the executed workflow.
    private static string HostOf(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ? uri.Host : url;
    }

    private static string FormatSeconds(long milliseconds)
    {
        return (milliseconds / (double)MILLISECONDS_PER_SECOND).ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Turns a <c>Retry-After</c> header into a wait, or null when there is nothing usable: no
    /// header, a value .NET could not parse (garbage, a negative delta), a zero delta, or a date
    /// already in the past. The wait is capped at <see cref="MAX_RETRY_AFTER_SECONDS"/>.
    /// </summary>
    internal static TimeSpan? ParseRetryAfter(RetryConditionHeaderValue? header, DateTimeOffset now)
    {
        if (header is null)
        {
            return null;
        }

        TimeSpan? wait = null;

        if (header.Delta is TimeSpan delta)
        {
            wait = delta;
        }
        else if (header.Date is DateTimeOffset date)
        {
            wait = date - now;
        }

        if (wait is not TimeSpan candidate || candidate <= TimeSpan.Zero)
        {
            return null;
        }

        TimeSpan cap = TimeSpan.FromSeconds(MAX_RETRY_AFTER_SECONDS);
        return candidate > cap ? cap : candidate;
    }

    private static bool TryAddHeader(
        HttpRequestHeaders headers,
        string key,
        string value,
        out string? error)
    {
        try
        {
            headers.Add(key, value);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void PopulateHeaders(
        HttpRequestHeaders target,
        string acceptHeader,
        Dictionary<string, string> headers,
        Action<string, string?>? onInvalidHeader = null)
    {
        if (!TryAddHeader(target, "Accept", acceptHeader, out string? acceptError))
        {
            onInvalidHeader?.Invoke("Accept", acceptError);
        }

        foreach (KeyValuePair<string, string> header in headers)
        {
            if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                continue; // Handle Content-Type separately for request content
            }

            if (!TryAddHeader(target, header.Key, header.Value, out string? error))
            {
                onInvalidHeader?.Invoke(header.Key, error);
            }
        }
    }

    private static bool HasEmbeddedPlaceholders(string? template)
    {
        return !string.IsNullOrWhiteSpace(template)
            && template.GetPlaceholders().Any()
            && !template.IsWholeStringSinglePlaceholder();
    }

    /// <summary>
    /// One round trip through <paramref name="handler"/>, the shared handler of the deployment's
    /// <see cref="OutboundAddressGuard"/> with the run's cookies: its pool is reused across
    /// requests, so the per-request client only carries the timeout. A refused address throws
    /// <see cref="OutboundAddressRefusedException"/>, which no retry catches.
    /// </summary>
    protected virtual async Task<string> ExecuteRequestAsync(
        HttpMessageHandler handler,
        string url,
        string method,
        string? payload,
        Dictionary<string, string> headers,
        string contentType,
        string acceptHeader,
        CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient(handler, disposeHandler: false)
        {
            Timeout = RequestTimeout
        };

        using var request = new HttpRequestMessage(new HttpMethod(method), url);

        PopulateHeaders(request.Headers, acceptHeader, headers);

        // Add payload for POST requests
        if (method.Equals("POST", StringComparison.InvariantCultureIgnoreCase)
            && !string.IsNullOrWhiteSpace(payload))
        {
            request.Content = new StringContent(payload, Encoding.UTF8, contentType);
        }

        // Execute request
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            cancellationToken);

        // Store the status (and any Retry-After) for the retry loop
        if (!response.IsSuccessStatusCode)
        {
            var ex = new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            ex.Data[STATUS_CODE_DATA_KEY] = response.StatusCode;

            if (ParseRetryAfter(response.Headers.RetryAfter, _timeProvider.GetUtcNow()) is TimeSpan retryAfter)
            {
                ex.Data[RETRY_AFTER_DATA_KEY] = retryAfter;
            }

            throw ex;
        }

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}

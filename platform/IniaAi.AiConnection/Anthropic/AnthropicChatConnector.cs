using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Anthropic.ApiModel;
using IniaAi.AiConnection.Anthropic.ApiModel.Bedrock;
using IniaAi.AiConnection.Anthropic.ApiModel.Enums;
using IniaAi.AiConnection.AwsBedrock;
using IniaAi.AiConnection.Azure;
using IniaAi.AiConnection.Common;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.AiConnection.Enums;
using IniaAi.AiConnection.HttpHandling;
using IniaAi.Common;
using IniaAi.Common.Content;
using IniaAi.Common.Enums;
using IniaAi.Common.Extensions;
using IniaAi.Configuration;
using IniaAi.Configuration.Enums.Anthropic;
using IniaAi.Configuration.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharpToken;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace IniaAi.AiConnection.Anthropic;

/// <summary>
/// Unified connector for the public Anthropic API https://api.anthropic.com/, Azure Foundry and a
/// model gateway (<see cref="IModelGateway"/>).
/// </summary>
public class AnthropicChatConnector : BaseChatConnector, IChatConnector
{
    #region Constants

    private const string API_KEY_HEADER = "X-Api-Key";
    private const string RETRY_AFTER_HEADER = "retry-after";
    private static readonly string[] _requestIdHeaders = ["request-id", "apim-request-id", "x-request-id"];
    private const string ANTHROPIC_VERSION_HEADER = "anthropic-version";
    // private const string ANTHROPIC_BETA_HEADER = "anthropic-beta";

    #endregion

    #region Private fields

    private readonly string _apiUrl = "https://api.anthropic.com/v1/messages";
    private readonly string _apiKey;
    private readonly string _apiVersion = "2023-06-01";
    private readonly bool _isAzureFoundry;
    private readonly bool _useEntraId;
    private readonly IAzureTokenProvider _tokenProvider;
    private readonly IModelGateway? _gateway;
    private readonly HttpClient _httpClient;
    private readonly PlatformSettings _platformSettings;
    private readonly ILogger<AnthropicChatConnector> _logger;
    private readonly GptEncoding _encoding;

    #endregion

    #region Properties

    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public int CacheCreationInputTokens { get; private set; }
    public int CacheReadInputTokens { get; private set; }

    private string ApiLabel => _gateway?.Name ?? (_isAzureFoundry ? "Azure Foundry" : "Anthropic Public API");

    #endregion

    #region Constructors

    public AnthropicChatConnector(
        PlatformSettings platformSettings,
        ILogger<AnthropicChatConnector> logger,
        TokenSlidingWindowRateLimiter tokenSlidingWindowRateLimiter,
        IAzureTokenProvider? azureTokenProvider = null,
        IModelGateway? gateway = null) : base(
            logger,
            maxConcurrency: platformSettings.Anthropic.MaxParallelCalls,
            tokenSlidingWindowRateLimiter
            )
    {
        _platformSettings = platformSettings;

        DeploymentMode deploymentMode = platformSettings.DeploymentMode;
        if (deploymentMode == DeploymentMode.Gateway)
        {
            _gateway = gateway ?? throw new InvalidOperationException(IModelGateway.NOT_IN_BUILD);
            _apiUrl = gateway.ChatUrl;
            _apiKey = "";
            _tokenProvider = new NullAzureTokenProvider();
        }
        else if (deploymentMode == DeploymentMode.Azure)
        {
            _isAzureFoundry = true;
            _apiUrl = platformSettings.AzureFoundry.AnthropicApiUrl
                ?? throw new InvalidOperationException("Azure Foundry Anthropic API URL not set.");
            _useEntraId = platformSettings.AzureFoundry.UseEntraId;

            if (_useEntraId)
            {
                _apiKey = "";
                _tokenProvider = azureTokenProvider
                    ?? throw new InvalidOperationException("Azure token provider required when UseEntraId is true.");
            }
            else
            {
                _apiKey = platformSettings.AzureFoundry.AnthropicApiKey
                    ?? throw new InvalidOperationException("Azure Foundry Anthropic API key not set.");
                _tokenProvider = new NullAzureTokenProvider();
            }

            _apiVersion = platformSettings.Anthropic.ApiVersion
                ?? throw new InvalidOperationException("Anthropic API version not set.");
        }
        else
        {
            _apiUrl = platformSettings.Anthropic.ApiUrl
                ?? throw new InvalidOperationException("Anthropic API URL not set.");
            _apiKey = platformSettings.Anthropic.ApiKey
                ?? throw new InvalidOperationException("Anthropic API key not set.");
            _apiVersion = platformSettings.Anthropic.ApiVersion
               ?? throw new InvalidOperationException("Anthropic API version not set.");
            _tokenProvider = new NullAzureTokenProvider();
        }

        _httpClient = CreateHttpClient();
        _logger = logger;

        _logger.LogInformation("AnthropicChatConnector initialized: {ApiLabel}, URL: {ApiUrl}", ApiLabel, _apiUrl);

        _encoding = GptEncoding.GetEncoding("cl100k_base");
    }

    public AnthropicChatConnector(
        IOptions<PlatformSettings> options,
        ILogger<AnthropicChatConnector> logger,
        TokenSlidingWindowRateLimiter tokenSlidingWindowRateLimiter,
        IAzureTokenProvider? azureTokenProvider = null,
        IModelGateway? gateway = null)
        : this(
            options.Value,
            logger,
            tokenSlidingWindowRateLimiter,
            azureTokenProvider,
            gateway)
    {
    }

    #endregion

    #region Public methods

    internal async Task<ResponseContentSequence> GetResponseAsync(
        object requestBody,
        bool shouldUseCaching = false,
        CancellationToken cancellationToken = default)
    {
        int estimatedTokens = EstimateTokens(requestBody);

        async Task<ResponseContentSequence> sendFunc()
        {
            var stopwatch = Stopwatch.StartNew();

            _logger.LogInformation(
                "Sending request to {ApiLabel} using caching: {ShouldUseCaching}, {RequestShape}",
                ApiLabel,
                shouldUseCaching,
                requestBody);

            string json = SerializeRequest(requestBody);

            _logger.LogDebug("LLM Request body: {Length} characters", json.Length);

            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var retryPolicy = new RetryPolicy
            {
                MaxRateLimitRetries = _platformSettings.Anthropic.MaxRateLimitRetryAttempts,
                MaxUnavailableRetries = _platformSettings.Anthropic.MaxServiceUnavailableRetryAttempts,
                RetryAfterHeader = RETRY_AFTER_HEADER,
                EnableTcpKeepAlive = false
            };

            SetRetryPolicyEnableTcpKeepAlive(requestBody, retryPolicy);

            Dictionary<string, string> additionalHeaders = [];

            if (_useEntraId)
            {
                string token = await _tokenProvider.GetTokenAsync(cancellationToken);
                additionalHeaders["Authorization"] = $"Bearer {token}";
            }

            if (_gateway is not null)
            {
                await _gateway.AddCallHeadersAsync(additionalHeaders, cancellationToken);
            }

            // --- Send HTTP ---
            var httpStopwatch = Stopwatch.StartNew();
            HttpResponseMessage response = await HttpRetryHandler.SendRequestWithRetryAsync(
                _httpClient,
                _apiUrl,
                content,
                _logger,
                policy: retryPolicy,
                additionalHeaders: additionalHeaders,
                cancellationToken: cancellationToken);

            string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            LogResponseHeaders(response);
            _logger.LogDebug("LLM Response: {Length} characters", responseJson.Length);

            _logger.LogTrace("LLM timings: Total={Total} ms | HTTP={Http} ms", stopwatch.ElapsedMilliseconds, httpStopwatch.ElapsedMilliseconds);

            // Always apply rate limit headers if present:
            if (response.Headers.TryGetValues("anthropic-ratelimit-input-tokens-remaining", out IEnumerable<string>? remaining) &&
                response.Headers.TryGetValues("anthropic-ratelimit-input-tokens-reset", out IEnumerable<string>? reset) &&
                response.Headers.TryGetValues("anthropic-ratelimit-input-tokens-limit", out IEnumerable<string>? limit))
            {
                if (int.TryParse(remaining.FirstOrDefault(), out int remainingTokens) &&
                    DateTimeOffset.TryParse(reset.FirstOrDefault(), null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTimeOffset resetUtc) &&
                    int.TryParse(limit.FirstOrDefault(), out int limitTokens))
                {
                    TokenRateLimiter.UpdateServerLimits(remainingTokens, resetUtc.UtcDateTime, limitTokens);
                }
            }

            if (requestBody is not RequestBody and not GatewayRequestBody)
            {
                throw new InvalidOperationException("Unsupported request body type. Expected RequestBody or GatewayRequestBody.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return HandleError(response.StatusCode, responseJson);
            }

            ResponseBody? successResponse = requestBody is RequestBody
                ? ResponseBody.FromJson(responseJson)
                : ResponseBody.FromJson(_gateway?.UnwrapResponse(responseJson));

            if (!response.IsSuccessStatusCode || successResponse is null)
            {
                return HandleError(response.StatusCode, responseJson);
            }

            InputTokens += successResponse.Usage?.InputTokens ?? 0;
            OutputTokens += successResponse.Usage?.OutputTokens ?? 0;
            CacheCreationInputTokens += successResponse.Usage?.CacheCreationInputTokens ?? 0;
            CacheReadInputTokens += successResponse.Usage?.CacheReadInputTokens ?? 0;

            _logger.LogInformation(
                "Tokens consumed: {InputTokens} in, {OutputTokens} out, {CacheCreationInputTokens} cache creation, {CacheReadInputTokens} cache read",
                successResponse.Usage?.InputTokens,
                successResponse.Usage?.OutputTokens,
                successResponse.Usage?.CacheCreationInputTokens,
                successResponse.Usage?.CacheReadInputTokens);

            CalculateEstimatedAgainstActualInputTokens(estimatedTokens, successResponse.Usage?.InputTokens ?? 0);

            if (successResponse.StopReason is StopReasonType.MaxTokens)
            {
                _logger.LogWarning("Inference stopped due to reaching the maximum token limit.");
            }

            ResponseContentSequence formatted = successResponse.FormatResponseContent();

            if (formatted.Refusal is ModelRefusal refusal)
            {
                string refusedModelId = refusal.ModelId
                    ?? ResolveRequestModel(requestBody)?.GetEnumMember()
                    ?? ModelProviders.UNKNOWN;

                ModelRefusalLog.Write(_logger, refusal, refusedModelId, ReadProviderRequestId(response));
            }

            return formatted;
        }

        ITunableRequestBody target = GetTunableRequestBody(requestBody);

        // Always wrap both backends:
        return await ThinkingExhaustion.SendAsync(
            effortToSend =>
            {
                target.OutputConfig = OutputConfig.FromEffort(effortToSend);
                return SendRequestWithGuardsAsync(estimatedTokens, sendFunc, cancellationToken);
            },
            ResolveRequestModel(requestBody),
            target.OutputConfig?.ToEffort(),
            _logger);
    }

    #endregion

    #region IChatConnector implementation

    /// <inheritdoc/>
    public async Task<ResponseContentSequence> GetResponseAsync(
        string userInput,
        string? systemPrompt = null,
        int maxTokens = -1,
        bool shouldUseAdvancedModel = true,
        string? model = null,
        ModelFamily? family = null,
        ModelEffort? effort = null,
        CancellationToken cancellationToken = default)
    {
        var message = new Message(new ContentRequest(new(userInput)));

        object requestBody = CreateRequestBody(
            message,
            systemPrompt,
            maxTokens,
            shouldUseAdvancedModel,
            shouldUseThinkingMode: false,
            model,
            effort);

        return await GetResponseAsync(
            requestBody,
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ResponseContentSequence> GetResponseAsync(
        RequestContentSequence content,
        string? systemPrompt = null,
        int maxTokens = -1,
        bool shouldUseAdvancedModel = true,
        string? model = null,
        ModelFamily? family = null,
        ModelEffort? effort = null,
        CancellationToken cancellationToken = default)
    {
        var message = new Message(content);

        object requestBody = CreateRequestBody(
            message,
            systemPrompt,
            maxTokens,
            shouldUseAdvancedModel,
            shouldUseThinkingMode: false,
            model,
            effort);

        return await GetResponseAsync(
            requestBody,
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ResponseContentSequence> GetResponseWithCachingAsync(
        RequestContentSequence content,
        RequestContentSequence cachedContent,
        string? systemPrompt = null,
        int maxTokens = -1,
        bool shouldUseAdvancedModel = true,
        string? model = null,
        ModelFamily? family = null,
        ModelEffort? effort = null,
        CancellationToken cancellationToken = default)
    {
        var message = new Message(ContentRequest.CreateCachedContent(cachedContent));
        message.Add(content);

        object requestBody = CreateRequestBody(
            message,
            systemPrompt,
            maxTokens,
            shouldUseAdvancedModel: true,
            shouldUseThinkingMode: false,
            model,
            effort);

        return await GetResponseAsync(
            requestBody,
            shouldUseCaching: true,
            cancellationToken: cancellationToken);
    }

    #endregion

    #region Thinking mode implementation

    /// <inheritdoc/>
    public async Task<ResponseContentSequence> GetThinkingResponseAsync(
        RequestContentSequence content,
        string? systemPrompt = null,
        int maxTokens = -1,
        int thinkingBudget = -1,
        bool shouldUseAdvancedModel = true,
        string? model = null,
        ModelEffort? effort = null,
        CancellationToken cancellationToken = default)
    {
        var message = new Message(content);

        object requestBody = CreateRequestBody(
            message,
            systemPrompt,
            maxTokens,
            shouldUseAdvancedModel,
            shouldUseThinkingMode: true,
            model);

        try
        {
            ApplyThinkingMode(requestBody, thinkingBudget, effort);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating thinking budget");

            return [new($"{IChatConnector.ERROR_PREFIX}{ex.Message}")];
        }

        return await GetResponseAsync(
            requestBody,
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ResponseContentSequence> GetThinkingResponseWithCachingAsync(
        RequestContentSequence content,
        RequestContentSequence cachedContent,
        string? systemPrompt = null,
        int maxTokens = -1,
        int thinkingBudget = -1,
        string? model = null,
        ModelEffort? effort = null,
        CancellationToken cancellationToken = default)
    {
        var message = new Message(ContentRequest.CreateCachedContent(cachedContent));
        message.Add(content);

        object requestBody = CreateRequestBody(
            message,
            systemPrompt,
            maxTokens,
            shouldUseAdvancedModel: true,
            shouldUseThinkingMode: true,
            model);

        try
        {
            ApplyThinkingMode(requestBody, thinkingBudget, effort);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating thinking budget");

            return [new($"{IChatConnector.ERROR_PREFIX}{ex.Message}")];
        }

        return await GetResponseAsync(
            requestBody,
            shouldUseCaching: true,
            cancellationToken: cancellationToken);
    }

    #endregion

    #region Private methods

    private int EstimateTokens(object requestBody)
    {
        int estimatedTokens;

        if (requestBody is RequestBody rb)
        {
            estimatedTokens = EstimateTokens(rb);
        }
        else if (requestBody is GatewayRequestBody gatewayRequest)
        {
            estimatedTokens = EstimateTokens(gatewayRequest.RequestBody);
        }
        else
        {
            estimatedTokens = 500; // fallback fudge
        }

        return estimatedTokens;
    }

    private void CalculateEstimatedAgainstActualInputTokens(int estimatedTokens, int actualInputTokens)
    {
        int diff = actualInputTokens - estimatedTokens;

        const int diffWarningThreshold = 500;

        if (Math.Abs(diff) > diffWarningThreshold)
        {
            _logger.LogWarning(
                "Token estimate drift: estimated={Estimated}, actual={Actual}, diff={Diff}. " +
                "Consider adjusting fudge factor or estimator logic.",
                estimatedTokens, actualInputTokens, diff);
        }
        else
        {
            _logger.LogTrace(
                "Token estimate accurate: estimated={Estimated}, actual={Actual}, diff={Diff}.",
                estimatedTokens, actualInputTokens, diff);
        }
    }

    private static void SetRetryPolicyEnableTcpKeepAlive(object requestBody, RetryPolicy retryPolicy)
    {
        if (requestBody is GatewayRequestBody gatewayRequest)
        {
            retryPolicy.EnableTcpKeepAlive = IsThinkingActive(requestBody, gatewayRequest.RequestBody.ThinkingSettings);
        }
        else if (requestBody is RequestBody req2)
        {
            retryPolicy.EnableTcpKeepAlive = IsThinkingActive(requestBody, req2.ThinkingSettings);
        }
    }

    /// <summary>
    /// Always-on-thinking models count as thinking-active even without a thinking
    /// configuration: they cannot switch thinking off and may reason for minutes
    /// on any request.
    /// </summary>
    private static bool IsThinkingActive(object requestBody, ThinkingSettings? thinkingSettings)
    {
        return thinkingSettings?.Type is ThinkingSettings.TYPE_ENABLED or ThinkingSettings.TYPE_ADAPTIVE
            || ResolveRequestModel(requestBody)?.IsThinkingAlwaysOnModel() is true;
    }

    private string SerializeRequest(object requestBody)
    {
        if (requestBody is not GatewayRequestBody gatewayRequest)
        {
            return JsonConvert.SerializeObject(requestBody, Formatting.Indented);
        }

        if (_gateway is null)
        {
            throw new InvalidOperationException(IModelGateway.NOT_IN_BUILD);
        }

        return _gateway.WrapRequest(gatewayRequest.RequestBody.ToJson(pretty: false), gatewayRequest.ModelName, gatewayRequest.ReadTimeoutSeconds);
    }

    private ResponseContentSequence HandleError(HttpStatusCode statusCode, string responseJson)
    {
        string errorText = _gateway?.ReadErrorText(responseJson) ?? ReadErrorText(responseJson, _isAzureFoundry);

        _logger.LogError(
            "Error occurred: {ErrorMessage}, HTTP status code: {StatusCode}",
            errorText,
            statusCode);

        return [new($"{IChatConnector.ERROR_PREFIX}{errorText}")];
    }

    internal static string ReadErrorText(string responseJson, bool isAzureFoundry)
    {
        var errorResponse = ErrorResponse.FromJson(responseJson);
        ErrorMessage? error = errorResponse?.Error;
        string? errorType = new[] { error?.ToErrorType()?.ToString(), error?.Type }
            .FirstOrDefault(type => !string.IsNullOrWhiteSpace(type));

        if (error?.Message is not null && errorType is not null)
        {
            return $"{errorType}: {error.Message}";
        }

        // An Azure APIM body names no error type, and its {"error": {...}} form reads like an untyped Anthropic error
        string? apimErrorText = isAzureFoundry ? TryParseAzureApimError(responseJson) : null;

        return apimErrorText ?? error?.Message ?? ReadDetailText(errorResponse?.Detail) ?? "Error occurred";
    }

    private static string? ReadDetailText(JToken? detail)
    {
        return detail?.Type switch
        {
            JTokenType.String => (string?)detail,
            JTokenType.Array or JTokenType.Object => detail.ToString(Formatting.None),
            _ => null
        };
    }

    /// <summary>
    /// Attempts to parse Azure APIM error response formats:
    /// Format 1: {"statusCode": 401, "message": "Access denied"}
    /// Format 2: {"error": {"code": "401", "message": "Invalid key"}}
    /// </summary>
    internal static string? TryParseAzureApimError(string responseJson)
    {
        try
        {
            var json = JObject.Parse(responseJson);

            // Format 1: {"statusCode": N, "message": "..."}
            if (json["statusCode"] is not null && json["message"] is not null)
            {
                return $"Azure APIM ({json["statusCode"]}): {json["message"]}";
            }

            // Format 2: {"error": {"code": "...", "message": "..."}}
            JToken? error = json["error"];
            if (error is JObject && error["message"] is not null)
            {
                string code = error["code"]?.ToString() ?? "unknown";
                return $"Azure APIM ({code}): {error["message"]}";
            }
        }
        catch
        {
            // JSON parsing failed — return null to fall through to generic error
        }

        return null;
    }

    internal static void ApplyThinkingMode(object requestBody, int thinkingBudget, ModelEffort? effort = null)
    {
        ITunableRequestBody target = GetTunableRequestBody(requestBody);
        AiModel? model = ResolveRequestModel(requestBody);

        if (ModelRequestShaper.TryApplyAdaptiveGenerationShape(target, model, shouldUseThinkingMode: true, effort))
        {
            return;
        }

        target.ThinkingSettings = new ThinkingSettings(thinkingBudget);
        target.Temperature = 1;

        ValidateThinkingBudget(target.MaxTokens, target.ThinkingSettings);
    }

    internal static void ApplyAdaptiveGenerationDefaults(object requestBody, ModelEffort? effort = null)
    {
        _ = ModelRequestShaper.TryApplyAdaptiveGenerationShape(
            GetTunableRequestBody(requestBody),
            ResolveRequestModel(requestBody),
            shouldUseThinkingMode: false,
            effort);
    }

    private static ITunableRequestBody GetTunableRequestBody(object requestBody)
    {
        return requestBody switch
        {
            RequestBody rb => rb,
            GatewayRequestBody gatewayRequest => gatewayRequest.RequestBody,
            _ => throw new InvalidOperationException("Unsupported request body type")
        };
    }

    private static AiModel? ResolveRequestModel(object requestBody)
    {
        return requestBody switch
        {
            RequestBody rb => ModelRequestShaper.ParseModelId(rb.ModelOverride) ?? rb.Model,
            GatewayRequestBody gatewayRequest => ModelRequestShaper.ParseModelId(gatewayRequest.ModelOverride) ?? gatewayRequest.ModelId,
            _ => null
        };
    }

    /// <summary>The provider's request id header: Anthropic's own, or the gateway's on Foundry and APIM.</summary>
    internal static string? ReadProviderRequestId(HttpResponseMessage response)
    {
        foreach (string header in _requestIdHeaders)
        {
            if (response.Headers.TryGetValues(header, out IEnumerable<string>? values)
                && values.FirstOrDefault() is string value
                && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private void LogResponseHeaders(HttpResponseMessage response)
    {
        var headers = response.Headers
            .Select(h => $"{h.Key}: {string.Join(", ", h.Value)}")
            .ToList();

        var contentHeaders = response.Content.Headers
            .Select(h => $"{h.Key}: {string.Join(", ", h.Value)}")
            .ToList();

        _logger.LogDebug("LLM Response Headers:\n{Headers}\n{ContentHeaders}",
            string.Join("\n", headers),
            string.Join("\n", contentHeaders));
    }

    private static void ValidateThinkingBudget(
        int maxTokens,
        ThinkingSettings? thinkingSettings)
    {
        if (thinkingSettings is null)
        {
            throw new ArgumentNullException(
                nameof(thinkingSettings),
                "Thinking budget is not set");
        }

        if (thinkingSettings.BudgetTokens > maxTokens)
        {
            throw new ArgumentException(
                $"Thinking budget cannot be greater than the maximum token limit, budget: {thinkingSettings.BudgetTokens}, max tokens: {maxTokens}",
                nameof(thinkingSettings));
        }

        if (thinkingSettings.BudgetTokens < 1024)
        {
            throw new ArgumentException(
                "Thinking budget must be at least 1024 tokens",
                nameof(thinkingSettings));
        }
    }

    /// <summary>
    /// Estimates the total number of input tokens for the LLM request body.
    /// </summary>
    /// <param name="requestBody">The request body.</param>
    /// <returns>Estimated token count.</returns>
    private int EstimateTokens(RequestBody requestBody)
    {
        return EstimateTokensCore(requestBody, requestBody.SystemPrompt);
    }

    /// <summary>
    /// Estimates the approximate number of input tokens for a given <see cref="BedrockRequestBody"/>.
    /// </summary>
    /// <param name="bedrock">The Bedrock request body.</param>
    /// <returns>Estimated token count.</returns>
    private int EstimateTokens(BedrockRequestBody bedrock)
    {
        return EstimateTokensCore(bedrock, systemPromptString: bedrock.SystemPrompt);
    }

    /// <summary>
    /// Approximate constant overhead per message block for roles/JSON.
    /// Tune for your request shape.
    /// </summary>
    public int EstimatedRoleOverhead { get; set; } = 5;

    /// <summary>
    /// Base overhead for system, version, etc.
    /// </summary>
    public int EstimatedBaseOverhead { get; set; } = 10;

    /// <summary>
    /// Approximate cost for each image placeholder.
    /// </summary>
    public int EstimatedImageTokenCost { get; set; } = 50;

    /// <summary>
    /// Empirical fudge overhead to cover system JSON glue, internal tokens, hidden context.
    /// Default is 1500, based on drift logs.
    /// </summary>
    public int EstimatedFudgeOverhead { get; set; } = 1500;

    /// <summary>
    /// Estimates total tokens for any request.
    /// </summary>
    /// <param name="messagesContainer">Request body.</param>
    /// <param name="systemPromptList">Optional Anthropic style system block.</param>
    /// <param name="systemPromptString">Optional single system string.</param>
    /// <param name="safetyMargin">Optional multiplier (e.g., 1.1 = +10%).</param>
    /// <returns>Estimated token count.</returns>
    internal int EstimateTokensCore(
        IMessagesContainer messagesContainer,
        IEnumerable<ContentRequest>? systemPromptList = null,
        string? systemPromptString = null,
        double safetyMargin = 1.1)
    {
        var sb = new StringBuilder();

        // Combine system prompt
        if (systemPromptList is not null)
        {
            foreach (ContentRequest sys in systemPromptList)
            {
                if (sys.Type is ContentType.Text && !string.IsNullOrWhiteSpace(sys.Text))
                {
                    _ = sb.Append(sys.Text).Append('\n');
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(systemPromptString))
        {
            _ = sb.Append(systemPromptString).Append('\n');
        }

        int imageCount = 0;
        int messageCount = 0;
        bool containsSummaryPattern = false;

        if (messagesContainer.Messages is not null)
        {
            foreach (Message message in messagesContainer.Messages)
            {
                messageCount++;

                if (message.Content is null)
                {
                    _logger.LogWarning("Message content is null, skipping.");
                    continue;
                }

                foreach (ContentRequest content in message.Content)
                {
                    if (content.CacheControl?.Type is CachingType.Ephemeral)
                    {
                        _logger.LogTrace("Skipping ephemeral content.");
                        continue;
                    }

                    switch (content.Type)
                    {
                        case ContentType.Text:
                        case ContentType.TextDelta:
                        case ContentType.Thinking:
                        case ContentType.ThinkingDelta:
                            if (!string.IsNullOrWhiteSpace(content.Text))
                            {
                                _ = sb.Append(content.Text).Append('\n');

                                // Heuristic: detect summary instructions in text
                                if (content.Text.Contains("summarize only", StringComparison.OrdinalIgnoreCase)
                                    || content.Text.Contains("Output in this exact format:", StringComparison.OrdinalIgnoreCase)
                                    || content.Text.Contains("<page", StringComparison.OrdinalIgnoreCase))
                                {
                                    containsSummaryPattern = true;
                                }
                            }

                            break;

                        case ContentType.Image:
                            imageCount++;
                            break;

                        default:
                            _logger.LogWarning(
                                "Unknown content type {Type} - ignored.",
                                content.Type);
                            break;
                    }
                }
            }
        }

        // Base tokens via BPE
        int bpeTokens = _encoding.Encode(sb.ToString()).Count;

        // Smart fudge factor:
        int fudgeOverhead = containsSummaryPattern
            ? 200  // Clamp fudge if short output likely
            : EstimatedFudgeOverhead;

        int structuralOverhead = (messageCount * EstimatedRoleOverhead) + EstimatedBaseOverhead;
        int imageOverhead = imageCount * EstimatedImageTokenCost;

        int estimated = bpeTokens + fudgeOverhead + structuralOverhead + imageOverhead;

        int final = (int)Math.Ceiling(estimated * safetyMargin);

        _logger.LogDebug(
            "EstimateTokensCore: BPE={BpeTokens} + Fudge={Fudge} + RoleOverhead={RoleOverhead} + Images={ImageOverhead} => Raw={Estimated} => Final={Final} (Messages={MessageCount}, Images={ImageCount}, SummaryPattern={SummaryPattern})",
            bpeTokens, fudgeOverhead, structuralOverhead, imageOverhead, estimated, final, messageCount, imageCount, containsSummaryPattern);

        return final;
    }

    private object CreateRequestBody(
        Message message,
        string? systemPrompt,
        int maxTokens,
        bool shouldUseAdvancedModel,
        bool shouldUseThinkingMode,
        string? model,
        ModelEffort? effort = null)
    {
        AiModel modelToUse = DetermineModelToUse(shouldUseAdvancedModel, model);
        maxTokens = DetermineMaxTokensToUse(shouldUseAdvancedModel, shouldUseThinkingMode, maxTokens, modelToUse);
        double temperature = DetermineTemperatureToUse(shouldUseAdvancedModel);

        if (_gateway is null)
        {
            var requestBody = new RequestBody(message, maxTokens, systemPrompt)
            {
                Model = modelToUse,
                Temperature = temperature
            };

            SetModelOverrideIfNeeded(shouldUseAdvancedModel, requestBody);

            if (!shouldUseThinkingMode)
            {
                ApplyAdaptiveGenerationDefaults(requestBody, effort);
            }

            return requestBody;
        }

        var bedrockRequestBody = new BedrockRequestBody(message, maxTokens, systemPrompt)
        {
            Temperature = temperature
        };

        var gatewayRequestBody = new GatewayRequestBody(bedrockRequestBody) { ModelId = modelToUse };

        SetModelOverrideIfNeeded(shouldUseAdvancedModel, gatewayRequestBody);

        if (!shouldUseThinkingMode)
        {
            ApplyAdaptiveGenerationDefaults(gatewayRequestBody, effort);
        }

        gatewayRequestBody.ReadTimeoutSeconds = _gateway.ReadTimeoutSeconds(
            shouldUseThinkingMode || ResolveRequestModel(gatewayRequestBody)?.IsThinkingAlwaysOnModel() is true);

        return gatewayRequestBody;
    }

    private void SetModelOverrideIfNeeded(
        bool shouldUseAdvancedModel,
        object requestBody)
    {
        string? overrideValue = GetSettingsModelIdOverride(shouldUseAdvancedModel);

        switch (requestBody)
        {
            case RequestBody rb:
                rb.ModelOverride = overrideValue;
                break;

            case GatewayRequestBody gatewayRequest:
                gatewayRequest.ModelOverride = overrideValue;
                break;

            default:
                throw new InvalidOperationException(
                    $"Unsupported request body type: {requestBody.GetType().Name}");
        }
    }

    private double DetermineTemperatureToUse(bool shouldUseAdvancedModel)
    {
        return shouldUseAdvancedModel
            ? _platformSettings.Anthropic.AdvancedModelTemperature
            : _platformSettings.Anthropic.BasicModelTemperature;
    }

    public VisionTier GetVisionTier(bool shouldUseAdvancedModel, string? model = null)
    {
        string? settingsModelIdOverride = GetSettingsModelIdOverride(shouldUseAdvancedModel);

        return settingsModelIdOverride is null
            ? SelectModel(shouldUseAdvancedModel, model).GetVisionTier()
            : VisionTier.ForModel(ModelRequestShaper.ParseModelId(settingsModelIdOverride));
    }

    public string GetModelId(bool shouldUseAdvancedModel, string? model = null)
    {
        return GetSettingsModelIdOverride(shouldUseAdvancedModel)
            ?? SelectModel(shouldUseAdvancedModel, model).GetEnumMember();
    }

    private AiModel GetSettingsModel(bool shouldUseAdvancedModel)
    {
        return shouldUseAdvancedModel
            ? _platformSettings.Anthropic.AdvancedModel
            : _platformSettings.Anthropic.BasicModel;
    }

    private string? GetSettingsModelIdOverride(bool shouldUseAdvancedModel)
    {
        string? settingsModelIdOverride = shouldUseAdvancedModel
            ? _platformSettings.Anthropic.AdvancedModelIdOverride
            : _platformSettings.Anthropic.BasicModelIdOverride;

        return string.IsNullOrWhiteSpace(settingsModelIdOverride) ? null : settingsModelIdOverride;
    }

    private AiModel DetermineModelToUse(bool shouldUseAdvancedModel, string? model)
    {
        AiModel modelToUse = SelectModel(shouldUseAdvancedModel, model);

        if (IsExplicitModel(shouldUseAdvancedModel, model))
        {
            _logger.LogInformation(
                "Using explicitly specified model: {Model}",
                modelToUse);
        }
        else
        {
            _logger.LogInformation(
                "Using model from platform settings: {default_model} (advanced: {ShouldUseAdvancedModel})",
                modelToUse, shouldUseAdvancedModel);
        }

        return modelToUse;
    }

    private AiModel SelectModel(bool shouldUseAdvancedModel, string? model)
    {
        AiModel defaultModel = GetSettingsModel(shouldUseAdvancedModel);

        return IsExplicitModel(shouldUseAdvancedModel, model)
            ? ModelRouteResolver.ResolveOrThrow(model!, defaultModel)
            : defaultModel;
    }

    /// <summary>
    /// The settings override replaces the model on the wire for every call of its tier, so a named
    /// model is not resolved and the request is shaped for the configured model instead.
    /// </summary>
    private bool IsExplicitModel(bool shouldUseAdvancedModel, string? model)
    {
        return !string.IsNullOrWhiteSpace(model) && GetSettingsModelIdOverride(shouldUseAdvancedModel) is null;
    }

    private int DetermineMaxTokensToUse(
        bool shouldUseAdvancedModel,
        bool shouldUseThinkingMode,
        int maxTokens,
        AiModel modelToUse)
    {
        int tokenCap = shouldUseThinkingMode && modelToUse.IsThinkingModel()
            ? 120000
            : modelToUse.GetMaxTokens()
            ?? (shouldUseAdvancedModel
                ? _platformSettings.Anthropic.AdvancedModelMaxTokens
                : _platformSettings.Anthropic.BasicModelMaxTokens);

        return Math.Min(tokenCap, maxTokens > 0 ? maxTokens : tokenCap);
    }

    private HttpClient CreateHttpClient()
    {
        HttpClientHandler handler = _gateway?.BypassesProxy is true
            ? new HttpClientHandler { UseProxy = false }
            : new HttpClientHandler();

        var httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(10)
        };

        // Skip the API key header when Entra ID or the gateway authenticates each call
        if (!_useEntraId && _gateway is null)
        {
            httpClient.DefaultRequestHeaders.Add(API_KEY_HEADER, _apiKey);
        }

        if (_gateway is null)
        {
            httpClient.DefaultRequestHeaders.Add(ANTHROPIC_VERSION_HEADER, _apiVersion);
        }

        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return httpClient;
    }

    #endregion
}

using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Util;
using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Anthropic.ApiModel;
using IniaAi.AiConnection.Common;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.AiConnection.HttpHandling;
using IniaAi.Common.Content;
using IniaAi.Common.Enums;
using IniaAi.Common.Extensions;
using IniaAi.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Polly;
using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using AnthropicAiModel = IniaAi.Configuration.Enums.Anthropic.AiModel;
using Message = IniaAi.AiConnection.Anthropic.ApiModel.Message;

namespace IniaAi.AiConnection.AwsBedrock;

/// <summary>
/// Unified AWS Bedrock chat provider that supports multiple model families.
/// </summary>
/// <remarks>
/// <para>
/// This provider uses a Polly resilience pipeline to handle transient failures when calling
/// AWS Bedrock APIs. The retry logic operates at the application level (above the AWS SDK
/// signing layer), ensuring that each retry attempt gets a fresh SigV4 signature.
/// </para>
/// <para>
/// AWS SigV4 signatures expire after 5 minutes. By retrying at the application level
/// rather than the HTTP level, we avoid signature expiration errors during extended
/// retry sequences caused by service unavailability (503) or throttling (429).
/// </para>
/// </remarks>
public class AwsBedrockChatProvider : BaseChatConnector, IChatConnector
{
    #region Private fields

    private readonly PlatformSettings _platformSettings;
    private readonly ILogger<AwsBedrockChatProvider> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Lazy<AmazonBedrockRuntimeClient> _bedrockClient;
    private readonly Dictionary<ModelFamily, IModelFormatAdapter> _formatAdapters;

    /// <summary>
    /// Polly resilience pipeline for handling transient AWS Bedrock failures.
    /// Operates above the AWS SDK signing layer to ensure fresh signatures on each retry.
    /// </summary>
    private readonly ResiliencePipeline<InvokeModelResponse> _resiliencePipeline;

    #endregion

    #region Properties

    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }

    /// <summary>
    /// Gets the last raw response JSON from the API. Useful for testing and verification.
    /// </summary>
    public string? LastRawResponseJson { get; private set; }

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="AwsBedrockChatProvider"/> class.
    /// </summary>
    /// <param name="platformSettings">Platform configuration settings.</param>
    /// <param name="logger">Logger for this provider.</param>
    /// <param name="loggerFactory">Factory for creating loggers for child components.</param>
    /// <param name="tokenSlidingWindowRateLimiter">Rate limiter for token-based throttling.</param>
    /// <remarks>
    /// <para>
    /// This constructor configures the AWS Bedrock client without HTTP-level retry handling.
    /// Instead, retry logic is implemented at the application level using Polly, which ensures
    /// that each retry attempt creates a new request with a fresh SigV4 signature.
    /// </para>
    /// <para>
    /// The resilience pipeline is configured to retry on transient failures including:
    /// throttling (429), service unavailable (503), and internal server errors (500).
    /// </para>
    /// </remarks>
    public AwsBedrockChatProvider(
        PlatformSettings platformSettings,
        ILogger<AwsBedrockChatProvider> logger,
        ILoggerFactory loggerFactory,
        TokenSlidingWindowRateLimiter tokenSlidingWindowRateLimiter) : base(
            logger,
            maxConcurrency: platformSettings.Anthropic.MaxParallelCalls,
            tokenSlidingWindowRateLimiter
            )
    {
        _platformSettings = platformSettings;
        _logger = logger;
        _loggerFactory = loggerFactory;

        _formatAdapters = new Dictionary<ModelFamily, IModelFormatAdapter>
        {
            [ModelFamily.Anthropic] = new AnthropicFormatAdapter(
                platformSettings,
                _loggerFactory.CreateLogger<AnthropicFormatAdapter>())
        };

        // Create retry policy from settings for both HTTP client factory and resilience pipeline.
        var retryPolicy = new RetryPolicy(_platformSettings.Anthropic);

        // The Bedrock client gets TCP keep-alive for long-running requests.
        // TCP keep-alive prevents network intermediaries from dropping idle connections during
        // model inference which can take 2-3 minutes.
        // Note: Retry logic is NOT in the HTTP client - it's handled at the application level.
        _bedrockClient = BedrockHttpClientFactory.LazyRuntimeClient(retryPolicy, logger);

        // Create resilience pipeline for handling transient failures with fresh signatures on each retry.
        // This operates above the AWS SDK signing layer, ensuring each retry gets a fresh SigV4 signature.
        _resiliencePipeline = BedrockResiliencePipelineFactory.Create(logger, retryPolicy);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AwsBedrockChatProvider"/> class using IOptions.
    /// </summary>
    /// <param name="options">Platform configuration settings wrapped in IOptions.</param>
    /// <param name="logger">Logger for this provider.</param>
    /// <param name="loggerFactory">Factory for creating loggers for child components.</param>
    /// <param name="tokenSlidingWindowRateLimiter">Rate limiter for token-based throttling.</param>
    public AwsBedrockChatProvider(
        IOptions<PlatformSettings> options,
        ILogger<AwsBedrockChatProvider> logger,
        ILoggerFactory loggerFactory,
        TokenSlidingWindowRateLimiter tokenSlidingWindowRateLimiter)
        : this(options.Value, logger, loggerFactory, tokenSlidingWindowRateLimiter)
    {
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
        string modelId = DetermineModelId(shouldUseAdvancedModel, model, family);
        ModelFamily modelFamily = DetermineModelFamily(modelId);
        double temperature = DetermineTemperature(shouldUseAdvancedModel, modelFamily);

        IModelFormatAdapter adapter = GetFormatAdapter(modelFamily);
        var message = new Message(new ContentRequest(new(userInput)));

        return await GetResponseInternalAsync(
            message,
            systemPrompt,
            effortToSend => adapter.CreateRequestFromText(
                userInput,
                systemPrompt,
                maxTokens,
                temperature,
                modelId,
                effortToSend),
            effort,
            modelId,
            maxTokens,
            adapter,
            cancellationToken);
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
        string modelId = DetermineModelId(shouldUseAdvancedModel, model, content, family);
        ModelFamily modelFamily = DetermineModelFamily(modelId);
        double temperature = DetermineTemperature(shouldUseAdvancedModel, modelFamily);

        IModelFormatAdapter adapter = GetFormatAdapter(modelFamily);
        var message = new Message(content);

        return await GetResponseInternalAsync(
            message,
            systemPrompt,
            effortToSend => adapter.CreateRequestFromContentSequence(
                content,
                systemPrompt,
                maxTokens,
                temperature,
                modelId,
                effortToSend),
            effort,
            modelId,
            maxTokens,
            adapter,
            cancellationToken);
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
        string modelId = DetermineModelId(shouldUseAdvancedModel, model, content, family);
        ModelFamily modelFamily = DetermineModelFamily(modelId);
        double temperature = DetermineTemperature(shouldUseAdvancedModel, modelFamily);

        IModelFormatAdapter adapter = GetFormatAdapter(modelFamily);
        var message = new Message(ContentRequest.CreateCachedContent(cachedContent));
        message.Add(content);

        return await GetResponseInternalAsync(
            message,
            systemPrompt,
            effortToSend => adapter.CreateRequestWithCachingFromContentSequence(
                content,
                cachedContent,
                systemPrompt,
                maxTokens,
                temperature,
                modelId,
                effortToSend),
            effort,
            modelId,
            maxTokens,
            adapter,
            cancellationToken);
    }

    #endregion

    #region Private methods

    /// <summary>
    /// Internal method that handles the actual AWS Bedrock API call with resilience.
    /// </summary>
    /// <param name="message">The message to send to the model.</param>
    /// <param name="systemPrompt">Optional system prompt.</param>
    /// <param name="createRequest">Creates the JSON request body at the given effort.</param>
    /// <param name="effort">The effort the caller asked for.</param>
    /// <param name="modelId">The AWS Bedrock model ID.</param>
    /// <param name="maxTokens">Maximum tokens in the response.</param>
    /// <param name="adapter">The model format adapter for request/response handling.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response content sequence from the model.</returns>
    /// <remarks>
    /// <para>
    /// This method uses a Polly resilience pipeline to handle transient failures.
    /// The InvokeModelAsync call is wrapped in the pipeline, ensuring that each retry
    /// creates a new request with a fresh SigV4 signature.
    /// </para>
    /// <para>
    /// Non-retryable exceptions (AccessDeniedException, ResourceNotFoundException,
    /// ValidationException) are caught and converted to error responses immediately.
    /// Transient exceptions (ThrottlingException, ServiceUnavailableException, etc.)
    /// are handled by the resilience pipeline with exponential backoff.
    /// </para>
    /// </remarks>
    private async Task<ResponseContentSequence> GetResponseInternalAsync(
        Message message,
        string? systemPrompt,
        Func<ModelEffort?, string> createRequest,
        ModelEffort? effort,
        string modelId,
        int maxTokens,
        IModelFormatAdapter adapter,
        CancellationToken cancellationToken)
    {
        int estimatedTokens = adapter.EstimateTokensFromMessage(message, systemPrompt);

        async Task<ResponseContentSequence> sendFunc(string requestJson)
        {
            var stopwatch = Stopwatch.StartNew();

            _logger.LogInformation(
                "Sending request to AWS Bedrock: Model: {ModelId}, Max tokens: {MaxTokens}, Family: {ModelFamily}, estimated tokens: {EstimatedTokens}",
                modelId,
                maxTokens,
                adapter.ModelFamily,
                estimatedTokens);

            // Make AWS Bedrock call with resilience pipeline.
            // The pipeline wraps InvokeModelAsync to handle transient failures with retry.
            // Each retry creates a new InvokeModelRequest, ensuring a fresh SigV4 signature.
            var httpStopwatch = Stopwatch.StartNew();

            InvokeModelResponse response;
            try
            {
                // Execute InvokeModelAsync through the resilience pipeline.
                // The request is created inside the lambda so each retry gets a fresh signature.
                response = await _resiliencePipeline.ExecuteAsync(
                    async ct =>
                    {
                        var invokeModelRequest = new InvokeModelRequest
                        {
                            ModelId = modelId,
                            Body = AWSSDKUtils.GenerateMemoryStreamFromString(requestJson),
                            ContentType = "application/json",
                            Accept = "application/json",
                        };

                        return await _bedrockClient.Value.InvokeModelAsync(invokeModelRequest, ct);
                    },
                    cancellationToken);
            }
            catch (Exception e)
            {
                // Handle non-retryable exceptions that should return error responses.
                // Transient exceptions (ThrottlingException, ServiceUnavailableException, etc.)
                // are already handled by the resilience pipeline and will be thrown if all retries fail.
                switch (e)
                {
                    case AccessDeniedException ade:
                        return [new($"{IChatConnector.ERROR_PREFIX}AccessDenied: {ade.Message}")];
                    case ResourceNotFoundException rne:
                        return [new($"{IChatConnector.ERROR_PREFIX}ModelNotFound: {rne.Message}")];
                    case ValidationException ve:
                        return [new($"{IChatConnector.ERROR_PREFIX}InvalidRequest: {ve.Message}")];
                    case ThrottlingException te:
                        // If we get here, all retries have been exhausted
                        return [new($"{IChatConnector.ERROR_PREFIX}RateLimitExceeded: {te.Message}")];
                    case ServiceUnavailableException sue:
                        // If we get here, all retries have been exhausted
                        return [new($"{IChatConnector.ERROR_PREFIX}ServiceUnavailable: {sue.Message}")];
                    case InternalServerException ise:
                        // If we get here, all retries have been exhausted
                        return [new($"{IChatConnector.ERROR_PREFIX}InternalServerError: {ise.Message}")];
                    case ModelTimeoutException mte:
                        // If we get here, all retries have been exhausted
                        return [new($"{IChatConnector.ERROR_PREFIX}ModelTimeout: {mte.Message}")];
                }

                throw;
            }

            string? responseJson;

            try
            {
                responseJson = JsonNode.Parse(response.Body)?.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse AWS Bedrock response body as JSON");

                throw new InvalidOperationException("Failed to parse response body from AWS Bedrock", ex);
            }

            if (string.IsNullOrWhiteSpace(responseJson))
            {
                _logger.LogError("AWS Bedrock returned empty or invalid response body");

                throw new InvalidOperationException("AWS Bedrock returned empty or invalid response body");
            }

            LogResponseHeaders(response);
            _logger.LogDebug("LLM Response: {Length} characters", responseJson.Length);

            _logger.LogTrace("LLM timings: Total={Total} ms | HTTP={Http} ms",
                stopwatch.ElapsedMilliseconds, httpStopwatch.ElapsedMilliseconds);

            if (response.HttpStatusCode is not HttpStatusCode.OK)
            {
                return HandleError(response.HttpStatusCode, responseJson);
            }

            ResponseContentSequence parsedResponse = adapter.ParseResponse(responseJson);
            parsedResponse.Metadata[ModelIdNormalizer.ENDPOINT_MODEL_METADATA_KEY] = modelId;

            // A body that names the plain model would read as the direct API to every usage
            // recorder that takes its provider from this id.
            if (!parsedResponse.Metadata.TryGetValue(ModelIdNormalizer.MODEL_METADATA_KEY, out object? answeredModel)
                || !ModelIdNormalizer.IsBedrockFormat(answeredModel as string))
            {
                parsedResponse.Metadata[ModelIdNormalizer.MODEL_METADATA_KEY] = modelId;
            }

            if (parsedResponse.Refusal is ModelRefusal refusal)
            {
                ModelRefusalLog.Write(_logger, refusal, modelId, response.ResponseMetadata?.RequestId);
            }

            LastRawResponseJson = responseJson;
            InputTokens += parsedResponse.InputTokens;
            OutputTokens += parsedResponse.OutputTokens;

            int cacheCreationTokens = parsedResponse.Metadata.TryGetValue("CacheCreationInputTokens", out object? cacheCreation)
                ? (int)cacheCreation
                : 0;
            int cacheReadTokens = parsedResponse.Metadata.TryGetValue("CacheReadInputTokens", out object? cacheRead)
                ? (int)cacheRead
                : 0;

            _logger.LogInformation(
                "Tokens consumed: {InputTokens} in, {OutputTokens} out, {CacheCreationInputTokens} cache creation, {CacheReadInputTokens} cache read (Family: {ModelFamily})",
                parsedResponse.InputTokens,
                parsedResponse.OutputTokens,
                cacheCreationTokens,
                cacheReadTokens,
                adapter.ModelFamily);

            CalculateEstimatedAgainstActualInputTokens(estimatedTokens, parsedResponse.InputTokens);

            if (parsedResponse.Metadata.TryGetValue("StopReason", out object? stopReason))
            {
                string? stopReasonStr = stopReason.ToString();

                if (stopReasonStr?.Contains("MaxTokens") == true)
                {
                    _logger.LogWarning("Inference stopped due to reaching the maximum token limit.");
                }
                else if (stopReasonStr?.Contains("ModelContextWindowExceeded") == true)
                {
                    _logger.LogWarning("Inference stopped because the request exceeded the model context window. Model: {ModelId}", modelId);
                }
            }

            return parsedResponse;
        }

        // Apply rate limiting and concurrency guards
        return await ThinkingExhaustion.SendAsync(
            effortToSend =>
            {
                string requestJson = createRequest(effortToSend);
                return SendRequestWithGuardsAsync(estimatedTokens, () => sendFunc(requestJson), cancellationToken);
            },
            ModelRequestShaper.ParseModelId(modelId),
            effort,
            _logger);
    }

    private IModelFormatAdapter GetFormatAdapter(ModelFamily family)
    {
        return !_formatAdapters.TryGetValue(family, out IModelFormatAdapter? adapter)
            ? throw new NotSupportedException($"Model family {family} is not supported by this provider")
            : adapter;
    }

    private ModelFamily DetermineModelFamily(string modelId)
    {
        foreach ((ModelFamily family, IModelFormatAdapter? adapter) in _formatAdapters)
        {
            if (adapter.SupportsModel(modelId))
            {
                return family;
            }
        }

        throw new NotSupportedException($"Model ID '{modelId}' is not supported by any registered format adapter");
    }

    private string DetermineModelId(
        bool shouldUseAdvancedModel,
        string? model,
        ModelFamily? family = null)
    {
        if (model is not null)
        {
            return ResolveRequestedModelId(shouldUseAdvancedModel, model);
        }

        ModelFamily targetFamily = family
            ?? ModelFamily.Anthropic;

        return targetFamily switch
        {
            ModelFamily.Anthropic => GetAnthropicModel(shouldUseAdvancedModel),
            _ => GetAnthropicModel(shouldUseAdvancedModel)
        };
    }

    private string DetermineModelId(
        bool shouldUseAdvancedModel,
        string? model,
        RequestContentSequence? content,
        ModelFamily? family = null)
    {
        if (model is not null)
        {
            return ResolveRequestedModelId(shouldUseAdvancedModel, model);
        }

        ModelFamily targetFamily = family
            ?? ModelFamily.Anthropic;

        return targetFamily switch
        {
            ModelFamily.Anthropic => GetAnthropicModel(shouldUseAdvancedModel),
            _ => GetAnthropicModel(shouldUseAdvancedModel)
        };
    }

    public VisionTier GetVisionTier(bool shouldUseAdvancedModel, string? model = null)
    {
        return ResolveModel(shouldUseAdvancedModel, model).GetVisionTier();
    }

    public string GetModelId(bool shouldUseAdvancedModel, string? model = null)
    {
        return ResolveModel(shouldUseAdvancedModel, model).GetEnumMember();
    }

    private AnthropicAiModel ResolveModel(bool shouldUseAdvancedModel, string? model)
    {
        AnthropicAiModel settingsModel = GetSettingsModel(shouldUseAdvancedModel);

        return model is null
            ? settingsModel
            : ModelRouteResolver.ResolveInStackRegionOrThrow(model, settingsModel);
    }

    private AnthropicAiModel GetSettingsModel(bool shouldUseAdvancedModel)
    {
        return shouldUseAdvancedModel
            ? _platformSettings.Anthropic.AdvancedModel
            : _platformSettings.Anthropic.BasicModel;
    }

    private string ResolveRequestedModelId(bool shouldUseAdvancedModel, string model)
    {
        string modelId = ResolveModel(shouldUseAdvancedModel, model).GetEnumMember();

        _logger.LogInformation(
            "Using explicitly specified model: {Model} (requested as {RequestedModel})",
            modelId,
            model);

        return modelId;
    }

    private string GetAnthropicModel(bool shouldUseAdvancedModel)
    {
        string modelId = GetSettingsModel(shouldUseAdvancedModel).GetEnumMember();

        _logger.LogInformation(
            "Using Anthropic model: {ModelId} (advanced: {ShouldUseAdvancedModel})",
            modelId,
            shouldUseAdvancedModel);

        return modelId
            ?? throw new InvalidOperationException("Failed to determine Anthropic model ID");
    }

    private double DetermineTemperature(bool shouldUseAdvancedModel, ModelFamily family)
    {
        return family switch
        {
            ModelFamily.Anthropic => shouldUseAdvancedModel
                ? _platformSettings.Anthropic.AdvancedModelTemperature
                : _platformSettings.Anthropic.BasicModelTemperature,
            _ => 0.2
        };
    }

    private ResponseContentSequence HandleError(HttpStatusCode statusCode, string responseJson)
    {
        string errorMessage = $"Error occurred: {statusCode}";

        try
        {
            dynamic? errorObj = JsonConvert.DeserializeObject<dynamic>(responseJson);

            if (errorObj?.error?.message is not null)
            {
                errorMessage = $"{errorObj.error.type}: {errorObj.error.message}";
            }
        }
        catch
        {
            errorMessage = string.IsNullOrWhiteSpace(responseJson)
                ? errorMessage
                : responseJson;
        }

        _logger.LogError(
            "Error occurred: {ErrorMessage}, HTTP status code: {StatusCode}",
            errorMessage,
            statusCode);

        return [new($"{IChatConnector.ERROR_PREFIX}{errorMessage}")];
    }

    private void LogResponseHeaders(InvokeModelResponse response)
    {
        try
        {
            var headerLines = new List<string>
            {
                $"HTTP Status: {response.HttpStatusCode}",
                $"Content Type: {response.ContentType}"
            };

            if (response.ResponseMetadata?.RequestId is not null)
            {
                headerLines.Add($"AWS Request ID: {response.ResponseMetadata.RequestId}");
            }

            if (response.ResponseMetadata?.Metadata is not null
                && response.ResponseMetadata.Metadata.Any())
            {
                foreach (KeyValuePair<string, string> kvp in response.ResponseMetadata.Metadata)
                {
                    headerLines.Add($"{kvp.Key}: {kvp.Value}");
                }
            }

            _logger.LogDebug(
                "LLM Response Headers:\n{Headers}",
                string.Join("\n", headerLines));
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Failed to log response headers");
        }
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
                estimatedTokens,
                actualInputTokens,
                diff);
        }
        else
        {
            _logger.LogTrace(
                "Token estimate accurate: estimated={Estimated}, actual={Actual}, diff={Diff}.",
                estimatedTokens,
                actualInputTokens,
                diff);
        }
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
        string modelId = DetermineModelId(shouldUseAdvancedModel, model, content, ModelFamily.Anthropic);
        ModelFamily modelFamily = DetermineModelFamily(modelId);

        if (modelFamily is not ModelFamily.Anthropic)
        {
            throw new NotSupportedException($"Thinking mode is only supported by Anthropic models, but {modelFamily} was specified");
        }

        LogThinkingMode(modelId, shouldUseAdvancedModel, thinkingBudget, effort);

        IModelFormatAdapter adapter = GetFormatAdapter(modelFamily);
        double temperature = 1.0; // Anthropic API requirement: extended thinking mode must use temperature = 1.0
        var message = new Message(content);

        return await GetResponseInternalAsync(
            message,
            systemPrompt,
            effortToSend => adapter.CreateThinkingRequestFromContentSequence(
                content,
                systemPrompt,
                maxTokens,
                thinkingBudget,
                temperature,
                modelId,
                effortToSend),
            effort,
            modelId,
            maxTokens,
            adapter,
            cancellationToken);
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
        string modelId = DetermineModelId(true, model, content, ModelFamily.Anthropic);
        ModelFamily modelFamily = DetermineModelFamily(modelId);

        if (modelFamily is not ModelFamily.Anthropic)
        {
            throw new NotSupportedException($"Thinking mode is only supported by Anthropic models, but {modelFamily} was specified");
        }

        LogThinkingMode(modelId, shouldUseAdvancedModel: true, thinkingBudget, effort);

        IModelFormatAdapter adapter = GetFormatAdapter(modelFamily);
        double temperature = 1.0; // Anthropic API requirement: extended thinking mode must use temperature = 1.0
        var message = new Message(content);

        return await GetResponseInternalAsync(
            message,
            systemPrompt,
            effortToSend => adapter.CreateThinkingRequestWithCachingFromContentSequence(
                content,
                cachedContent,
                systemPrompt,
                maxTokens,
                thinkingBudget,
                temperature,
                modelId,
                effortToSend),
            effort,
            modelId,
            maxTokens,
            adapter,
            cancellationToken);
    }

    private void LogThinkingMode(string modelId, bool shouldUseAdvancedModel, int thinkingBudget, ModelEffort? effort)
    {
        AnthropicAiModel? model = ModelRequestShaper.ParseModelId(modelId);

        if (model?.IsAdaptiveThinkingModel() is true)
        {
            _logger.LogInformation(
                "Using adaptive thinking with model: {ModelId} (advanced: {ShouldUseAdvancedModel}), effort: {Effort}",
                modelId,
                shouldUseAdvancedModel,
                effort ?? model.Value.GetDefaultEffort());
            return;
        }

        _logger.LogInformation(
            "Using thinking mode with model: {ModelId} (advanced: {ShouldUseAdvancedModel}), thinking budget: {ThinkingBudget}",
            modelId,
            shouldUseAdvancedModel,
            thinkingBudget);
    }

    #endregion
}

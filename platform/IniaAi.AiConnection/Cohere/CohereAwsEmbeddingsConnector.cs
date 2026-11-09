using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Util;
using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.AwsBedrock;
using IniaAi.AiConnection.Cohere.ApiModel.Embeddings;
using IniaAi.AiConnection.HttpHandling;
using IniaAi.Common.Extensions;
using IniaAi.Configuration;
using IniaAi.Configuration.Enums.Cohere;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using System.Text.Json.Nodes;

namespace IniaAi.AiConnection.Cohere;

/// <summary>
/// Cohere embeddings connector for AWS Bedrock.
/// </summary>
/// <remarks>
/// <para>
/// This connector uses a Polly resilience pipeline to handle transient failures when calling
/// AWS Bedrock APIs. The retry logic operates at the application level (above the AWS SDK
/// signing layer), ensuring that each retry attempt gets a fresh SigV4 signature.
/// </para>
/// <para>
/// AWS SigV4 signatures expire after 5 minutes. By retrying at the application level
/// rather than the HTTP level, we avoid signature expiration errors during extended
/// retry sequences caused by service unavailability (503) or throttling (429).
/// </para>
/// </remarks>
public class CohereAwsEmbeddingsConnector : IEmbeddingsConnector
{
    private readonly ILogger<IEmbeddingsConnector> _logger;
    private readonly Lazy<AmazonBedrockRuntimeClient> _bedrockClient;
    private readonly PlatformSettings _platformSettings;

    /// <summary>
    /// Polly resilience pipeline for handling transient AWS Bedrock failures.
    /// Operates above the AWS SDK signing layer to ensure fresh signatures on each retry.
    /// </summary>
    private readonly ResiliencePipeline<InvokeModelResponse> _resiliencePipeline;

    public int TotalTokens { get; private set; }

    public string ModelName => $"Cohere: {GetEmbeddingsModel()}";

    public string ModelId =>
        // Return the actual API model ID (EnumMember value) for pricing lookup
        // For AWS Bedrock models like "global.cohere.embed-v4:0", ModelIdNormalizer will handle normalization
        GetEmbeddingsModel().GetEnumMember()
                ?? GetEmbeddingsModel().ToString();

    /// <summary>
    /// Initializes a new instance of the <see cref="CohereAwsEmbeddingsConnector"/> class.
    /// </summary>
    /// <param name="logger">Logger for this connector.</param>
    /// <param name="options">Platform configuration settings wrapped in IOptions.</param>
    /// <remarks>
    /// <para>
    /// This constructor configures the AWS Bedrock client with TCP keep-alive configured
    /// for long-running requests. Retry logic is implemented at the application level using
    /// Polly, ensuring that each retry attempt creates a new request with a fresh SigV4 signature.
    /// </para>
    /// </remarks>
    public CohereAwsEmbeddingsConnector(
        ILogger<IEmbeddingsConnector> logger,
        IOptions<PlatformSettings> options)
    {
        _logger = logger;
        _platformSettings = options.Value;

        // Create retry policy from settings for both HTTP client factory and resilience pipeline.
        var retryPolicy = new RetryPolicy(_platformSettings.Cohere);

        // The Bedrock client gets TCP keep-alive for long-running requests.
        // TCP keep-alive prevents network intermediaries from dropping idle connections.
        // Note: Retry logic is NOT in the HTTP client - it's handled at the application level.
        _bedrockClient = BedrockHttpClientFactory.LazyRuntimeClient(retryPolicy, logger);

        // Create resilience pipeline for handling transient failures with fresh signatures on each retry.
        // This operates above the AWS SDK signing layer, ensuring each retry gets a fresh SigV4 signature.
        _resiliencePipeline = BedrockResiliencePipelineFactory.Create(logger, retryPolicy);
    }

    public async Task<List<double[]>> GetResponseAsync(string document, CancellationToken cancellationToken = default)
    {
        var requestBody = new RequestBody(document)
        {
            Model = GetEmbeddingsModel(),
            OutputDimension = _platformSettings.Cohere.EmbeddingsOutputDimension
        };

        return await GetResponseAsync(requestBody, cancellationToken);
    }

    public Task<List<double[]>> GetResponseAsync(List<string> documents, CancellationToken cancellationToken = default)
    {
        var requestBody = new RequestBody(documents)
        {
            Model = GetEmbeddingsModel(),
            OutputDimension = _platformSettings.Cohere.EmbeddingsOutputDimension
        };

        return GetResponseAsync(requestBody, cancellationToken);
    }

    /// <summary>
    /// Sends a request to Cohere via AWS Bedrock with resilience handling.
    /// </summary>
    /// <param name="requestBody">The request body to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The embeddings from the model.</returns>
    /// <remarks>
    /// <para>
    /// This method uses a Polly resilience pipeline to handle transient failures.
    /// The InvokeModelAsync call is wrapped in the pipeline, ensuring that each retry
    /// creates a new request with a fresh SigV4 signature.
    /// </para>
    /// </remarks>
    private async Task<List<double[]>> GetResponseAsync(
        RequestBody requestBody,
        CancellationToken cancellationToken = default)
    {
        string modelId = requestBody.Model?.GetEnumMember()
            ?? throw new InvalidOperationException("Cohere model not set in the request body.");

        requestBody.Model = null;

        _logger.LogDebug(
            "Sending request to Cohere embeddings API using {ModelId}",
            modelId);

        string json = requestBody.ToJson(pretty: true);

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
                        Body = AWSSDKUtils.GenerateMemoryStreamFromString(json),
                        ContentType = "application/json",
                        Accept = "application/json",
                    };

                    return await _bedrockClient.Value.InvokeModelAsync(invokeModelRequest, ct);
                },
                cancellationToken);
        }
        catch (Exception e)
        {
            // Handle non-retryable exceptions that should return empty results.
            // Transient exceptions (ThrottlingException, ServiceUnavailableException, etc.)
            // are already handled by the resilience pipeline and will be thrown if all retries fail.
            switch (e)
            {
                case AccessDeniedException ade:
                    _logger.LogError(ade, "Access denied when calling Cohere embeddings API");
                    return [];
                case ResourceNotFoundException rne:
                    _logger.LogError(rne, "Model not found when calling Cohere embeddings API");
                    return [];
                case ValidationException ve:
                    _logger.LogError(ve, "Invalid request when calling Cohere embeddings API");
                    return [];
                case ThrottlingException te:
                    // If we get here, all retries have been exhausted
                    _logger.LogError(te, "Rate limit exceeded when calling Cohere embeddings API (all retries exhausted)");
                    return [];
                case ServiceUnavailableException sue:
                    // If we get here, all retries have been exhausted
                    _logger.LogError(sue, "Service unavailable when calling Cohere embeddings API (all retries exhausted)");
                    return [];
                case InternalServerException ise:
                    // If we get here, all retries have been exhausted
                    _logger.LogError(ise, "Internal server error when calling Cohere embeddings API (all retries exhausted)");
                    return [];
                case ModelTimeoutException mte:
                    // If we get here, all retries have been exhausted
                    _logger.LogError(mte, "Model timeout when calling Cohere embeddings API (all retries exhausted)");
                    return [];
            }

            throw;
        }

        string? responseJson = JsonNode.Parse(response.Body)?.ToString();

        if (response.HttpStatusCode is not System.Net.HttpStatusCode.OK
            || ResponseBody.FromJson(responseJson) is not ResponseBody successResponse
            || successResponse is null)
        {
            _logger.LogError(
                "Error response from Cohere embeddings API. Status code: {statusCode}, Response: {error}",
                response.HttpStatusCode, response.HttpStatusCode is System.Net.HttpStatusCode.OK ? $"{responseJson?.Length ?? 0} characters" : responseJson);

            return [];
        }

        // Use BilledUnits for v2 API (primary), with Tokens as fallback for older API versions
        int inputTokens = successResponse.Meta.BilledUnits.InputTokens > 0
            ? successResponse.Meta.BilledUnits.InputTokens
            : successResponse.Meta.Tokens.InputTokens;
        int outputTokens = successResponse.Meta.BilledUnits.OutputTokens > 0
            ? successResponse.Meta.BilledUnits.OutputTokens
            : successResponse.Meta.Tokens.OutputTokens;
        TotalTokens += inputTokens + outputTokens;

        _logger.LogDebug(
            "Cohere embeddings API usage. Total tokens: {totalTokens} (input: {inputTokens}, output: {outputTokens})",
            TotalTokens, inputTokens, outputTokens);

        _logger.LogDebug(
            "Cohere embeddings generated: {num}",
            successResponse.Embeddings.Length);

        return [.. successResponse.Embeddings];
    }

    private AiModel GetEmbeddingsModel()
    {
        return _platformSettings.Cohere.EmbeddingsModel switch
        {
            AiModel.AwsEmbedEnglishV30 when !PlatformSettings.IsRunningInAws => AiModel.EmbedEnglishV30,
            AiModel.AwsEmbedMultilingualV30 when !PlatformSettings.IsRunningInAws => AiModel.EmbedMultilingualV30,
            AiModel.AwsEmbedV40 when !PlatformSettings.IsRunningInAws => AiModel.EmbedV40,
            AiModel.EmbedEnglishV30 when PlatformSettings.IsRunningInAws => AiModel.AwsEmbedEnglishV30,
            AiModel.EmbedMultilingualV30 when PlatformSettings.IsRunningInAws => AiModel.AwsEmbedMultilingualV30,
            AiModel.EmbedV40 when PlatformSettings.IsRunningInAws => AiModel.AwsEmbedV40,
            _ => _platformSettings.Cohere.EmbeddingsModel,
        };
    }
}

using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Azure;
using IniaAi.AiConnection.Azure.ApiModel;
using IniaAi.AiConnection.HttpHandling;
using IniaAi.Common.Extensions;
using IniaAi.Configuration;
using IniaAi.Configuration.Enums.Cohere;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;

namespace IniaAi.AiConnection.Cohere;

/// <summary>
/// Cohere embeddings connector for Azure AI Foundry.
/// Uses the Azure AI Model Inference API (/models/embeddings) with
/// Azure-specific auth (api-key header or Entra ID bearer token).
/// </summary>
public class AzureCohereEmbeddingsConnector : IEmbeddingsConnector
{
    private const string API_KEY_HEADER = "api-key";

    private readonly string _apiUrl;
    private readonly string _deploymentName;
    private readonly bool _useEntraId;
    private readonly IAzureTokenProvider _tokenProvider;
    private readonly HttpClient _httpClient;
    private readonly ILogger<IEmbeddingsConnector> _logger;
    private readonly PlatformSettings _platformSettings;

    public int TotalTokens { get; private set; }

    public string ModelName => $"Cohere (Azure): {GetEmbeddingsModel()}";

    public string ModelId => GetEmbeddingsModel().GetEnumMember()
                ?? GetEmbeddingsModel().ToString();

    public AzureCohereEmbeddingsConnector(
        ILogger<IEmbeddingsConnector> logger,
        IOptions<PlatformSettings> options,
        IAzureTokenProvider? azureTokenProvider = null)
    {
        _platformSettings = options.Value;
        _logger = logger;

        _apiUrl = options.Value.AzureFoundry.CohereApiUrl
            ?? throw new InvalidOperationException("Azure Foundry Cohere API URL not set.");

        _deploymentName = options.Value.AzureFoundry.CohereDeploymentName
            ?? throw new InvalidOperationException(
                "Azure Foundry CohereDeploymentName not set. " +
                "Set it to the Azure deployment name (e.g. \"cohere-embed-v4\").");

        _useEntraId = options.Value.AzureFoundry.UseEntraId;

        if (_useEntraId)
        {
            _tokenProvider = azureTokenProvider
                ?? throw new InvalidOperationException("Azure token provider required when UseEntraId is true.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(options.Value.AzureFoundry.CohereApiKey))
            {
                throw new InvalidOperationException("Azure Foundry Cohere API key not set.");
            }

            _tokenProvider = new NullAzureTokenProvider();
        }

        _httpClient = CreateHttpClient(options.Value.AzureFoundry.CohereApiKey);
    }

    public async Task<List<double[]>> GetResponseAsync(string document, CancellationToken cancellationToken = default)
    {
        return await GetResponseAsync([document], cancellationToken);
    }

    public Task<List<double[]>> GetResponseAsync(List<string> documents, CancellationToken cancellationToken = default)
    {
        return GetResponseInternalAsync(documents, cancellationToken);
    }

    private async Task<List<double[]>> GetResponseInternalAsync(
        List<string> documents,
        CancellationToken cancellationToken = default)
    {
        var requestBody = new EmbeddingsRequest
        {
            Input = [.. documents],
            Model = _deploymentName,
            Dimensions = _platformSettings.Cohere.EmbeddingsOutputDimension,
        };

        _logger.LogDebug(
            "Sending request to Azure AI Inference embeddings API, deployment: {Deployment}, configured model: {Model}",
            _deploymentName, _platformSettings.Cohere.EmbeddingsModel);

        var content = new StringContent(
            requestBody.ToJson(pretty: false),
            Encoding.UTF8, "application/json");

        var retryPolicy = new RetryPolicy
        {
            MaxRateLimitRetries = _platformSettings.Cohere.MaxRateLimitRetryAttempts,
            MaxUnavailableRetries = _platformSettings.Cohere.MaxServiceUnavailableRetryAttempts,
        };

        Dictionary<string, string>? additionalHeaders = null;

        if (_useEntraId)
        {
            string token = await _tokenProvider.GetTokenAsync(cancellationToken);
            additionalHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {token}"
            };
        }

        HttpResponseMessage response = await HttpRetryHandler.SendRequestWithRetryAsync(
            _httpClient,
            _apiUrl,
            content,
            _logger,
            policy: retryPolicy,
            additionalHeaders: additionalHeaders,
            cancellationToken: cancellationToken);

        string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode
            || EmbeddingsResponse.FromJson(responseJson) is not EmbeddingsResponse successResponse)
        {
            _logger.LogError(
                "Error response from Azure AI Inference embeddings API. Status code: {statusCode}, Response: {error}",
                response.StatusCode, response.IsSuccessStatusCode ? $"{responseJson.Length} characters" : responseJson);

            return [];
        }

        TotalTokens += successResponse.Usage.TotalTokens;

        _logger.LogDebug(
            "Azure AI Inference embeddings API usage. Total tokens: {totalTokens} (prompt: {promptTokens})",
            TotalTokens, successResponse.Usage.PromptTokens);

        _logger.LogDebug(
            "Azure AI Inference embeddings generated: {num}",
            successResponse.Data.Length);

        // Return embeddings ordered by index to match input order
        return [.. successResponse.Data
            .OrderBy(d => d.Index)
            .Select(d => d.Embedding)];
    }

    /// <summary>
    /// Normalizes the configured embeddings model — maps AWS-specific variants
    /// to their standard equivalents since Azure is not AWS.
    /// </summary>
    private AiModel GetEmbeddingsModel()
    {
        return _platformSettings.Cohere.EmbeddingsModel switch
        {
            AiModel.AwsEmbedEnglishV30 => AiModel.EmbedEnglishV30,
            AiModel.AwsEmbedMultilingualV30 => AiModel.EmbedMultilingualV30,
            AiModel.AwsEmbedV40 => AiModel.EmbedV40,
            _ => _platformSettings.Cohere.EmbeddingsModel,
        };
    }

    private HttpClient CreateHttpClient(string? apiKey)
    {
        var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // For API key mode, set the Azure-style api-key header
        if (!_useEntraId && !string.IsNullOrWhiteSpace(apiKey))
        {
            httpClient.DefaultRequestHeaders.Add(API_KEY_HEADER, apiKey);
        }

        return httpClient;
    }
}

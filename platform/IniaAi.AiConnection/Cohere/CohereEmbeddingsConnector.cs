using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Cohere.ApiModel.Embeddings;
using IniaAi.AiConnection.Common;
using IniaAi.AiConnection.HttpHandling;
using IniaAi.Common.Extensions;
using IniaAi.Configuration;
using IniaAi.Configuration.Enums.Cohere;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;

namespace IniaAi.AiConnection.Cohere;

public class CohereEmbeddingsConnector : IEmbeddingsConnector
{
    private const string API_KEY_NOT_SET = "Cohere API key not set.";
    private const string API_KEY_SETTING = "PlatformSettings:Cohere:ApiKey";

    private readonly string _apiUrl = "https://api.cohere.com/v2/embed";
    private readonly string? _apiKey;
    private readonly HttpClient _httpClient;
    private readonly ILogger<IEmbeddingsConnector> _logger;
    private readonly PlatformSettings _platformSettings;
    private readonly AiModel _embeddingsModel;

    public string Provider => ModelProviders.COHERE_API;

    public string? MissingSetting => string.IsNullOrEmpty(_apiKey) ? API_KEY_SETTING : null;

    public string ModelName => $"Cohere: {_embeddingsModel}";

    public string ModelId => _embeddingsModel.GetEnumMember();

    public CohereEmbeddingsConnector(
        ILogger<IEmbeddingsConnector> logger,
        IOptions<PlatformSettings> options)
    {
        _platformSettings = options.Value;
        _embeddingsModel = CohereEmbeddingsRoute.CohereApiForm(_platformSettings.Cohere.EmbeddingsModel);
        _logger = logger;
        _apiUrl = options.Value.Cohere.EmbeddingsApiUrl
            ?? throw new InvalidOperationException("Cohere embeddings API URL not set.");
        // A deployment without a Cohere key still builds this connector, so that everything that
        // needs no embeddings works. The first call refuses before it sends anything.
        _apiKey = options.Value.Cohere.ApiKey;
        _httpClient = CreateHttpClient();
    }

    public Task<EmbeddingsAnswer> EmbedAsync(List<string> documents, CancellationToken cancellationToken = default)
    {
        var requestBody = new RequestBody(documents)
        {
            Model = _embeddingsModel,
            OutputDimension = _platformSettings.Cohere.EmbeddingsOutputDimension,
            EmbeddingTypes = ["float"]
        };

        return EmbedAsync(requestBody, cancellationToken);
    }

    private async Task<EmbeddingsAnswer> EmbedAsync(
        RequestBody requestBody,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            throw new ApiKeyNotSetException(API_KEY_NOT_SET);
        }

        _logger.LogDebug(
            "Sending request to Cohere embeddings API using {Model}",
            requestBody.Model);

        var content = new StringContent(
            requestBody.ToJson(pretty: false),
            Encoding.UTF8, "application/json");

        var retryPolicy = new RetryPolicy
        {
            MaxRateLimitRetries = _platformSettings.Cohere.MaxRateLimitRetryAttempts,
            MaxUnavailableRetries = _platformSettings.Cohere.MaxServiceUnavailableRetryAttempts,
        };

        HttpResponseMessage response = await HttpRetryHandler.SendRequestWithRetryAsync(
            _httpClient,
            _apiUrl,
            content,
            _logger,
            policy: retryPolicy,
            cancellationToken: cancellationToken);

        string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode
            || ResponseBody.FromJson(responseJson) is not ResponseBody successResponse
            || successResponse is null)
        {
            _logger.LogError(
                "Error response from Cohere embeddings API. Status code: {statusCode}, Response: {error}",
                response.StatusCode, response.IsSuccessStatusCode ? $"{responseJson.Length} characters" : responseJson);

            return EmbeddingsAnswer.Empty;
        }

        // Use BilledUnits for v2 API (primary), with Tokens as fallback for older API versions
        int inputTokens = successResponse.Meta.BilledUnits.InputTokens > 0
            ? successResponse.Meta.BilledUnits.InputTokens
            : successResponse.Meta.Tokens.InputTokens;

        _logger.LogDebug(
            "Cohere embeddings API usage. Input tokens: {inputTokens}",
            inputTokens);

        _logger.LogDebug(
            "Cohere embeddings generated: {num}",
            successResponse.Embeddings.Length);

        return new EmbeddingsAnswer([.. successResponse.Embeddings], inputTokens);
    }

    private HttpClient CreateHttpClient()
    {
        var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        return httpClient;
    }
}

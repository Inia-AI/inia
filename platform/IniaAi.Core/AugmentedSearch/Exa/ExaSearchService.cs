using IniaAi.Common.Http;
using IniaAi.Configuration;
using IniaAi.Core.AugmentedSearch.Exa.Interfaces;
using IniaAi.Core.Dto.Exa.Search;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Text;

namespace IniaAi.Core.AugmentedSearch.Exa;

public class ExaSearchService : IExaSearchService
{
    // The service is built once per block, so its clients share one handler and its connection pool.
    private static readonly SocketsHttpHandler _sharedHandler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    };

    private static readonly JsonSerializerSettings _requestSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore
    };

    private static readonly JsonSerializerSettings _responseSettings = new()
    {
        ContractResolver = new DefaultContractResolver
        {
            NamingStrategy = new CamelCaseNamingStrategy()
        }
    };

    private readonly HttpClient _httpClient;
    private readonly ExaSettings _exaSettings;
    private readonly ILogger<ExaSearchService> _logger;

    public ExaSearchService(
        PlatformSettings platformSettings,
        ILogger<ExaSearchService> logger)
    {
        _exaSettings = platformSettings.Exa;
        _logger = logger;

        var loggingHandler = new HttpLoggingHandler(logger, new HttpLoggingOptions
        {
            ServiceName = "Exa",
        })
        {
            InnerHandler = _sharedHandler
        };

        _httpClient = new HttpClient(loggingHandler, disposeHandler: false)
        {
            Timeout = TimeSpan.FromMinutes(_exaSettings.TimeoutMinutes)
        };

        _httpClient.DefaultRequestHeaders.Add("x-api-key", _exaSettings.ApiKey);
    }

    public ExaSearchService(
        IOptions<PlatformSettings> platformSettings,
        ILogger<ExaSearchService> logger) : this(platformSettings.Value, logger)
    {
    }

    public async Task<ExaSearchResponse?> SearchAsync(
        ExaSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Performing Exa search for a query of {Length} characters", request.Query.Length);

            ExaSearchRequest validatedRequest = request;

            if (request.IncludeDomains is not null && request.IncludeDomains.Length > 0)
            {
                string[] validIncludeDomains = ValidateAndFilterDomains(request.IncludeDomains, "IncludeDomains");
                validatedRequest = validatedRequest with
                {
                    IncludeDomains = validIncludeDomains.Length > 0
                        ? validIncludeDomains
                        : null
                };
            }

            if (request.ExcludeDomains is not null && request.ExcludeDomains.Length > 0)
            {
                string[] validExcludeDomains = ValidateAndFilterDomains(request.ExcludeDomains, "ExcludeDomains");
                validatedRequest = validatedRequest with
                {
                    ExcludeDomains = validExcludeDomains.Length > 0
                        ? validExcludeDomains
                        : null
                };
            }

            string requestJson = JsonConvert.SerializeObject(validatedRequest, _requestSettings);
            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            string endpoint = $"{_exaSettings.ExaApiUrl.TrimEnd('/')}/search";

            HttpResponseMessage response = await _httpClient.PostAsync(
                endpoint,
                content,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                string responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

                ExaSearchResponse? result = JsonConvert.DeserializeObject<ExaSearchResponse>(
                    responseContent,
                    _responseSettings);

                _logger.LogInformation(
                    "Successfully completed Exa search, found {ResultCount} results, request_id: {RequestId}",
                    result?.Results.Length ?? 0,
                    result?.RequestId ?? "null");

                return result;
            }

            string errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Exa search request failed with status: {StatusCode}, error: {Error}",
                response.StatusCode,
                errorContent);

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing search with Exa service");
            return null;
        }
    }

    private string[] ValidateAndFilterDomains(string[] domains, string parameterName)
    {
        var validDomains = new List<string>();

        foreach (string domain in domains)
        {
            if (string.IsNullOrWhiteSpace(domain))
            {
                _logger.LogWarning(
                    "Skipping empty or whitespace domain in {ParameterName}",
                    parameterName);
                continue;
            }

            if (Uri.TryCreate(domain, UriKind.Absolute, out Uri? uri)
                && (uri.Scheme == "http" || uri.Scheme == "https"))
            {
                validDomains.Add(uri.Host);

                if (domain != uri.Host)
                {
                    _logger.LogDebug(
                        "Extracted host {Host} from URL {Url} in {ParameterName}",
                        uri.Host,
                        domain,
                        parameterName);
                }
            }
            else if (Uri.CheckHostName(domain) is not UriHostNameType.Unknown)
            {
                validDomains.Add(domain);
            }
            else
            {
                _logger.LogWarning(
                    "Skipping invalid domain: {Domain} in {ParameterName}",
                    domain,
                    parameterName);
            }
        }

        if (validDomains.Count < domains.Length)
        {
            _logger.LogWarning(
                "Filtered out {InvalidCount} invalid domains from {ParameterName}. Proceeding with {ValidCount} valid domains",
                domains.Length - validDomains.Count,
                parameterName,
                validDomains.Count);
        }

        return [.. validDomains];
    }
}

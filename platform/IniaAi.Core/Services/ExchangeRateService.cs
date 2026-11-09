using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Dto.ExchangeRate;
using IniaAi.Core.Services.Abstraction;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace IniaAi.Core.Services;

public class ExchangeRateService : IExchangeRateService
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ExchangeRateService> _logger;
    private readonly ExchangeRateApiSettings _settings;
    private readonly AsyncRetryPolicy<decimal> _retryPolicy;

    public ExchangeRateService(
        HttpClient httpClient,
        IMemoryCache cache,
        ILogger<ExchangeRateService> logger,
        IOptions<PlatformSettings> options)
    {
        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;
        _settings = options?.Value?.ExchangeRateApi ?? throw new ArgumentNullException(nameof(options));

        _retryPolicy = Policy<decimal>
            .Handle<HttpRequestException>()
            .WaitAndRetryAsync(
                _settings.MaxRetryAttempts,
                retryAttempt => TimeSpan.FromMilliseconds(_settings.RetryDelayMilliseconds * retryAttempt),
                onRetry: (result, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning(result.Exception,
                        "Retry {RetryCount} after {Delay}ms due to {ExceptionType}: {Message}",
                        retryCount, timeSpan.TotalMilliseconds, result.Exception?.GetType().Name, result.Exception?.Message);
                });
    }

    private async Task<decimal> GetRateFromApiAsync(
        string fromCurrency,
        string toCurrency,
        DateTime date)
    {
        string requestUrl = $"{_settings.BaseUrl.TrimEnd('/')}/v6/{_settings.ApiKey}/history/{fromCurrency}/{date.Year}/{date.Month}/{date.Day}";

        _logger.LogDebug("Requesting {FromCurrency} exchange rates for {Date:yyyy-MM-dd}", fromCurrency, date);

        string? stringResponse = await _httpClient.GetStringAsync(requestUrl);

        _logger.LogDebug("Received response: {Length} characters", stringResponse?.Length ?? 0);

        var response = ExchangeRateResponse.FromJson(stringResponse);

        if (response?.Result == "error")
        {
            _logger.LogError("Error response from exchange rate API: {ErrorType}", response.ErrorType);

            throw new InvalidOperationException(
                $"Failed to get exchange rate for {fromCurrency} to {toCurrency} on {date:yyyy-MM-dd}. Error: {response.ErrorType}");
        }

        if (response?.ConversionRates is null || !response.ConversionRates.TryGetValue(toCurrency, out decimal rate))
        {
            _logger.LogError("Exchange rate for {ToCurrency} not found in response for {FromCurrency} on {Date}",
                toCurrency, fromCurrency, date);

            throw new InvalidOperationException(
                $"Exchange rate for {toCurrency} not found in response for {fromCurrency} on {date:yyyy-MM-dd}");
        }

        await CacheAllRatesAsync(fromCurrency, date, response.ConversionRates);

        _logger.LogInformation("Exchange rate for {FromCurrency} to {ToCurrency} on {Date}: {Rate}",
            fromCurrency, toCurrency, date, rate);

        return rate;
    }

    public async Task<decimal> GetExchangeRateAsync(string fromCurrency, string toCurrency, DateTime date)
    {
        // Validate date parameter
        if (date.Date > DateTime.UtcNow.Date)
        {
            throw new ArgumentException("Cannot get exchange rates for future dates", nameof(date));
        }

        // Validate date range
        if (date < new DateTime(1990, 1, 1))
        {
            throw new ArgumentException("Historical data is only available from January 1, 1990", nameof(date));
        }

        // If currencies are the same, return 1
        if (string.Equals(fromCurrency, toCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return 1m;
        }

        // Try to get from cache
        string cacheKey = GetCacheKey(fromCurrency, toCurrency, date);
        if (_cache.TryGetValue(cacheKey, out decimal cachedRate))
        {
            _logger.LogDebug("Cache hit for {FromCurrency} to {ToCurrency} on {Date}", fromCurrency, toCurrency, date);
            return cachedRate;
        }

        // If not in cache, call the API
        return await _retryPolicy.ExecuteAsync(() => GetRateFromApiAsync(fromCurrency, toCurrency, date));
    }

    private static string GetCacheKey(string fromCurrency, string toCurrency, DateTime date)
    {
        return $"ExchangeRate_{fromCurrency}_{toCurrency}_{date:yyyy-MM-dd}";
    }

    private async Task CacheRateAsync(string cacheKey, decimal rate)
    {
        MemoryCacheEntryOptions cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(_settings.CacheExpirationMinutes));

        _ = _cache.Set(cacheKey, rate, cacheOptions);
        await Task.CompletedTask;
    }

    private async Task CacheAllRatesAsync(string baseCurrency, DateTime date, Dictionary<string, decimal> rates)
    {
        foreach ((string targetCurrency, decimal rate) in rates)
        {
            if (string.Equals(baseCurrency, targetCurrency, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string cacheKey = GetCacheKey(baseCurrency, targetCurrency, date);
            await CacheRateAsync(cacheKey, rate);

            // Also cache the reverse rate
            decimal reverseRate = 1m / rate;
            string reverseCacheKey = GetCacheKey(targetCurrency, baseCurrency, date);
            await CacheRateAsync(reverseCacheKey, reverseRate);
        }
    }
}

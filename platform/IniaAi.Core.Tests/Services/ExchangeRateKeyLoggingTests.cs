using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Services;
using IniaAi.Testing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace IniaAi.Core.Tests.Services;

/// <summary>
/// The exchange-rate API takes its key in the request path, so a log line that names the request
/// would carry the key into every log store that keeps it.
/// </summary>
public class ExchangeRateKeyLoggingTests
{
    private const string API_KEY = "exchange-rate-key-that-must-not-be-logged";

    [Fact]
    public async Task ALookup_LogsNoKey()
    {
        var log = new CapturingLogger<ExchangeRateService>();
        var settings = new PlatformSettings
        {
            ExchangeRateApi = new ExchangeRateApiSettings { BaseUrl = "https://rates.example.com", ApiKey = API_KEY },
        };
        var service = new ExchangeRateService(
            new HttpClient(new CannedExchangeRatesHandler()),
            new MemoryCache(new MemoryCacheOptions()),
            log,
            Options.Create(settings));

        decimal rate = await service.GetExchangeRateAsync("USD", "EUR", DateTime.UtcNow.Date.AddDays(-1));

        Assert.Equal(0.9m, rate);
        Assert.NotEmpty(log.Entries);
        Assert.All(log.Entries, entry =>
        {
            Assert.DoesNotContain(API_KEY, entry.Message);
            Assert.DoesNotContain(entry.Values, value => value.Value?.ToString()?.Contains(API_KEY) == true);
        });
    }
}

using IniaAi.Core.Services;
using IniaAi.Testing;
using IniaAi.Testing.XunitAttributes;
using Microsoft.Extensions.Caching.Memory;

namespace IniaAi.Core.Tests.Services;

public class ExchangeRateServiceTests : IClassFixture<ExchangeRateFixture<ExchangeRateService>>
{
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly ExchangeRateFixture<ExchangeRateService> _fixture;
    private readonly ExchangeRateService _service;
    private readonly IMemoryCache _memoryCache;

    public ExchangeRateServiceTests(
        ExchangeRateFixture<ExchangeRateService> fixture,
        ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
        _fixture = fixture;
        _service = _fixture.GetExchangeRateService(_testOutputHelper);
        _memoryCache = _fixture.GetMemoryCache();
    }

    [Fact]
    public async Task GetExchangeRateAsync_SameCurrency_ReturnsOne()
    {
        decimal result = await _service.GetExchangeRateAsync("USD", "USD", DateTime.UtcNow);
        Assert.Equal(1m, result);
    }

    [Fact]
    public async Task GetExchangeRateAsync_CachedRate_ReturnsCachedValue()
    {
        // Arrange
        string cacheKey = $"ExchangeRate_USD_EUR_{DateTime.UtcNow:yyyy-MM-dd}";
        _ = _memoryCache.Set(cacheKey, 1.5m);

        // Act
        decimal result = await _service.GetExchangeRateAsync("USD", "EUR", DateTime.UtcNow);

        Assert.Equal(1.5m, result);
    }

    [Fact]
    public async Task GetExchangeRateAsync_WithOnlyTheReverseRateCached_FetchesTheRateFromTheApi()
    {
        DateTime date = DateTime.UtcNow;
        _ = _memoryCache.Set($"ExchangeRate_JPY_GBP_{date:yyyy-MM-dd}", 0.005m);

        decimal rate = await _service.GetExchangeRateAsync("GBP", "JPY", date);

        Assert.Equal(CannedExchangeRatesHandler.Rate("GBP", "JPY"), rate);
        Assert.True(_memoryCache.TryGetValue($"ExchangeRate_GBP_JPY_{date:yyyy-MM-dd}", out decimal _), "The fetched rate was not cached.");
    }

    [Fact]
    public async Task GetExchangeRateAsync_ForACurrencyTheApiDoesNotSupport_FailsWithTheApisErrorType()
    {
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.GetExchangeRateAsync("XXX", "EUR", DateTime.UtcNow));

        Assert.Contains("unsupported-code", error.Message);
    }

    [PaidApiFact("Calls the live exchange-rate API with the key in PLATFORMSETTINGS__EXCHANGERATEAPI__APIKEY", alsoInCi: true)]
    public async Task GetExchangeRateAsync_FromTheLiveApi_ReturnsARate()
    {
        ExchangeRateService service = _fixture.GetLiveExchangeRateService(_testOutputHelper);

        decimal rate = await service.GetExchangeRateAsync("USD", "SEK", DateTime.UtcNow.Date.AddDays(-1));

        Assert.True(rate > 0m, $"The live API returned a USD to SEK rate of {rate}.");
    }
}

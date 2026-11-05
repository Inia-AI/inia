using IniaAi.Core.Services;
using IniaAi.Testing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace IniaAi.Core.Tests.Services;

public class ExchangeRateFixture<T> : TestsBaseFixture<T> where T : class
{
    private readonly IMemoryCache _memoryCache;

    public ExchangeRateFixture()
    {
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
    }

    public IMemoryCache GetMemoryCache()
    {
        return _memoryCache;
    }

    public ExchangeRateService GetExchangeRateService(ITestOutputHelper testOutputHelper)
    {
        return CreateService(new HttpClient(new CannedExchangeRatesHandler()), testOutputHelper);
    }

    public ExchangeRateService GetLiveExchangeRateService(ITestOutputHelper testOutputHelper)
    {
        return CreateService(new HttpClient(), testOutputHelper);
    }

    private ExchangeRateService CreateService(HttpClient httpClient, ITestOutputHelper testOutputHelper)
    {
        return new ExchangeRateService(
            httpClient,
            _memoryCache,
            TestsLogger.CreateLogger<ExchangeRateService>(testOutputHelper),
            Options.Create(PlatformSettings));
    }
}

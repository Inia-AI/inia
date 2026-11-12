using IniaAi.Core.Services.Abstraction;
using IniaAi.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.WebApi.IntegrationTests;

/// <summary>
/// The API host these tests start reaches no outside service: a workflow with an exchange-rate block gets its rates
/// from fixed answers, not from the live API.
/// </summary>
public sealed class TestHostNetworkTests
{
    [Fact]
    public async Task AnExchangeRateLookup_IsAnsweredInsideTheTestHost()
    {
        await using var factory = new CustomWebApplicationFactory();
        IHttpMessageHandlerFactory handlers = factory.Services.GetRequiredService<IHttpMessageHandlerFactory>();

        HttpMessageHandler handler = handlers.CreateHandler(nameof(IExchangeRateService));
        while (handler is DelegatingHandler delegating)
        {
            handler = delegating.InnerHandler!;
        }

        _ = Assert.IsType<CannedExchangeRatesHandler>(handler);
    }
}

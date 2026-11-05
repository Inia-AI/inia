using System.Net;
using System.Text;
using System.Text.Json;

namespace IniaAi.Testing;

/// <summary>
/// Answers the exchange-rate API's history request at the network edge with fixed rates, so the
/// exchange-rate service and block run without the live API or its key. Each rate is derived from
/// one table of US dollar rates, so a rate and its reverse agree.
/// </summary>
public sealed class CannedExchangeRatesHandler : HttpMessageHandler
{
    private const string HISTORY_SEGMENT = "history";

    private static readonly Dictionary<string, decimal> _perUsDollar = new(StringComparer.OrdinalIgnoreCase)
    {
        ["USD"] = 1m,
        ["EUR"] = 0.9m,
        ["GBP"] = 0.8m,
        ["CHF"] = 0.88m,
        ["SEK"] = 10.5m,
        ["JPY"] = 150m,
        ["AUD"] = 1.5m,
        ["NZD"] = 1.65m,
    };

    /// <summary>The rate this handler answers for one unit of <paramref name="from"/> in <paramref name="to"/>.</summary>
    public static decimal Rate(string from, string to)
    {
        return _perUsDollar[to] / _perUsDollar[from];
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string[] segments = request.RequestUri!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string baseCurrency = segments[Array.IndexOf(segments, HISTORY_SEGMENT) + 1];

        Dictionary<string, object> body = _perUsDollar.ContainsKey(baseCurrency)
            ? new()
            {
                ["result"] = "success",
                ["base_code"] = baseCurrency,
                ["conversion_rates"] = _perUsDollar.Keys.ToDictionary(currency => currency, currency => Rate(baseCurrency, currency)),
            }
            : new()
            {
                ["result"] = "error",
                ["error-type"] = "unsupported-code",
            };

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        });
    }
}

using System.Text;
using IniaAi.Common.Http;
using IniaAi.Testing;

namespace IniaAi.Common.Tests.Http;

/// <summary>
/// The shared HTTP logging handler writes each call's method, address, status, timing, redacted
/// headers and body sizes, never a body: a body can carry a customer's documents or what a model
/// wrote about them.
/// </summary>
public sealed class HttpLoggingHandlerTests : IDisposable
{
    private const string BODY_MARKER = "QUOKKA-BODY-5A7E";
    private const string KEY_MARKER = "QUOKKA-KEY-1C9F";

    private readonly LocalTestHttpServer _site = new();

    public void Dispose()
    {
        _site.Dispose();
    }

    [Fact]
    public async Task ACall_LogsBodySizes_NeitherBody_NorTheApiKey()
    {
        var logger = new CapturingLogger("Exa");
        using var client = new HttpClient(new HttpLoggingHandler(logger, new HttpLoggingOptions { ServiceName = "Exa" }) { InnerHandler = new HttpClientHandler() });
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_site.BaseUrl}/search")
        {
            Content = new StringContent($"{{\"query\":\"{BODY_MARKER} net assets\"}}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-api-key", KEY_MARKER);

        using HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Contains(BODY_MARKER, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(2, logger.Entries.Count);
        string[] leaks = logger.Entries.Holding(BODY_MARKER, KEY_MARKER);
        Assert.True(leaks.Length == 0, $"{leaks.Length} log lines carry a body or the key:{Environment.NewLine}{string.Join(Environment.NewLine, leaks)}");
        Assert.NotNull(logger.Entries[0].Value("RequestBytes"));
    }
}

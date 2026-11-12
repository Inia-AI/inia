using System.Net;
using IniaAi.Infrastructure.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Infrastructure.Tests.Hosting;

/// <summary>
/// A health endpoint answers the methods its callers use, GET and HEAD, and refuses every other, TRACE included
/// (ASVS 13.4.4). The framework's plain mapping answers TRACE, which is what the read-only mapping closes.
/// </summary>
public sealed class HealthEndpointsTests : IAsyncLifetime
{
    private readonly HttpClient _client = new();
    private WebApplication _app = null!;
    private Uri _base = null!;

    public async ValueTask InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = ["--urls", "http://127.0.0.1:0"] });
        _ = builder.Services.AddHealthChecks();

        _app = builder.Build();
        _ = _app.MapReadOnlyHealthChecks("/health");
        _ = _app.MapHealthChecks("/plain");
        await _app.StartAsync(TestContext.Current.CancellationToken);
        _base = new Uri(_app.Urls.Single());
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task AReadOnlyHealthEndpoint_AnswersGetAndHead_AndRefusesEveryOtherMethod()
    {
        HttpStatusCode[] statuses = await StatusesAsync("/health", HttpMethod.Get, HttpMethod.Head, HttpMethod.Trace, HttpMethod.Post, HttpMethod.Delete);

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.MethodNotAllowed, HttpStatusCode.MethodNotAllowed, HttpStatusCode.MethodNotAllowed], statuses);
    }

    [Fact]
    public async Task APlainHealthMapping_AnswersTrace()
    {
        HttpStatusCode[] statuses = await StatusesAsync("/plain", HttpMethod.Trace);

        Assert.Equal([HttpStatusCode.OK], statuses);
    }

    private async Task<HttpStatusCode[]> StatusesAsync(string path, params HttpMethod[] methods)
    {
        var statuses = new List<HttpStatusCode>();
        foreach (HttpMethod method in methods)
        {
            using var request = new HttpRequestMessage(method, new Uri(_base, path));
            using HttpResponseMessage response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
            statuses.Add(response.StatusCode);
        }

        return [.. statuses];
    }
}

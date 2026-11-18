using System.Net;
using IniaAi.Infrastructure.Hosting;
using IniaAi.WebApi.UnitTests.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IniaAi.WebApi.UnitTests.Configuration;

/// <summary>
/// The readiness report lists every internal check, and a failing one's error can name the database host, so only a
/// caller on the API's own host reads it. Every other caller, such as a Kubernetes probe from the node, gets the
/// status code and the overall status alone, whatever case the path is written in.
/// </summary>
public sealed class ReadinessEndpointTests(ReadinessEndpointTests.ReadinessHost host) : IClassFixture<ReadinessEndpointTests.ReadinessHost>
{
    private const string READINESS = "/readiness";
    private const string CHECK_NAME = "database";
    private const string CHECK_DETAIL = "db.internal.example:5432";

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public async Task ACallerOnTheHost_GetsTheFullReport(string caller)
    {
        (int status, string body) = await CallAsync(READINESS, caller);

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Contains(CHECK_NAME, body, StringComparison.Ordinal);
        Assert.Contains(CHECK_DETAIL, body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/readiness")]
    [InlineData("/READINESS")]
    [InlineData("/Readiness")]
    public async Task ACallerFromOutside_GetsTheStatusAlone(string path)
    {
        (int status, string body) = await CallAsync(path, "203.0.113.7");

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Equal(nameof(HealthStatus.Healthy), body);
    }

    private async Task<(int Status, string Body)> CallAsync(string path, string caller)
    {
        HttpContext context = await host.App.GetTestServer().SendAsync(request =>
        {
            request.Request.Path = path;
            request.Connection.RemoteIpAddress = IPAddress.Parse(caller);
        });
        return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>One API host that maps the readiness report, shared by the tests that only call it.</summary>
    public sealed class ReadinessHost : IAsyncLifetime
    {
        public WebApplication App { get; private set; } = null!;

        public async ValueTask InitializeAsync()
        {
            WebApplicationBuilder builder = TestWebApplication.CreateBuilder();
            _ = builder.Services.AddHealthChecks().AddCheck(CHECK_NAME, () => HealthCheckResult.Healthy(CHECK_DETAIL));
            App = builder.Build();
            _ = App.MapHealthChecksWithHostOnlyReport(READINESS, new HealthCheckOptions
            {
                ResponseWriter = (context, report) => context.Response.WriteAsync(
                    string.Join(", ", report.Entries.Select(entry => $"{entry.Key}: {entry.Value.Description}"))),
            });
            await App.StartAsync(TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
        }
    }
}

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.WebApi.UnitTests.Common;

/// <summary>
/// The builder of an in-process test host that reads no settings files or environment variables
/// and adds no logging providers. The default builder does all three for every host, and a test
/// class that starts a host per test pays for them each time.
/// </summary>
public static class TestWebApplication
{
    public static WebApplicationBuilder CreateBuilder()
    {
        WebApplicationBuilder builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        _ = builder.WebHost.UseTestServer();
        _ = builder.Services.AddRouting();
        _ = builder.Services.AddLogging();
        return builder;
    }
}

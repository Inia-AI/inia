using IniaAi.WebApi.Configuration;
using IniaAi.WebApi.Controllers.v1.Base;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Net;

namespace IniaAi.WebApi.UnitTests.Common;

/// <summary>
/// The API's controllers behind its authentication and authorization middleware, and nothing else.
/// A request signs in as the role its header names, and a role gate answers before any filter or
/// action runs, so a refusal needs none of the services behind the endpoint. A request carries all
/// it needs, so a test class shares one host as a class fixture.
/// </summary>
public sealed class RoleGateHost : IAsyncLifetime
{
    private const string USER_ID = "role-gate-user";

    private WebApplication? _app;
    private HttpClient _client = null!;

    public IServiceProvider Services => _app!.Services;

    public async ValueTask InitializeAsync()
    {
        WebApplicationBuilder builder = TestWebApplication.CreateBuilder();
        _ = builder.Services.AddConfiguredControllers();
        _ = builder.Services.AddConfiguredApiVersioning();
        _ = builder.Services.AddControllers().AddApplicationPart(typeof(ApiController).Assembly);
        _ = builder.Services.AddAuthentication(TestHeaderAuthentication.SCHEME).AddScheme<AuthenticationSchemeOptions, TestHeaderAuthentication>(TestHeaderAuthentication.SCHEME, _ => { });
        _ = builder.Services.AddAuthorization();

        _app = builder.Build();
        _ = _app.UseRouting();
        _ = _app.UseAuthentication();
        _ = _app.UseAuthorization();
        _ = _app.MapControllers();
        await _app.StartAsync(TestContext.Current.CancellationToken);
        _client = _app.GetTestClient();
    }

    /// <summary>The status a signed-in caller who holds only <paramref name="role"/> gets back.</summary>
    public async Task<HttpStatusCode> SendAsAsync(HttpMethod method, string path, string role)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add(TestHeaderAuthentication.USER_HEADER, USER_ID);
        request.Headers.Add(TestHeaderAuthentication.ROLE_HEADER, role);
        using HttpResponseMessage response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}

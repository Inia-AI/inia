using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.WebApi.Configuration;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using IniaAi.WebApi.Middleware;
using IniaAi.WebApi.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.WebApi.UnitTests.Configuration;

/// <summary>
/// An in-memory host with the API's own controller, OData and versioning composition, serving the
/// probe controllers of this test assembly.
/// </summary>
public sealed class ApiPipelineHost : IAsyncLifetime
{
    private WebApplication? _app;

    public HttpClient Client { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        _ = builder.WebHost.UseTestServer();
        _ = builder.Services.AddConfiguredControllers();
        _ = builder.Services.AddConfiguredApiVersioning();
        _ = builder.Services.AddControllers().AddApplicationPart(typeof(ApiPipelineHost).Assembly);
        _ = builder.Services.AddAuthorization();
        _ = builder.Services.AddScoped<IRequestContext, RequestContext>();

        _app = builder.Build();
        _ = _app.UseRouting();
        _ = _app.UseMiddleware<ApiVersionReportingMiddleware>();
        _ = _app.UseAuthorization();
        _ = _app.MapControllers();
        await _app.StartAsync();
        Client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}

[ApiController]
[Route("api/v{version:apiVersion}/odata-probe")]
public class ODataProbeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get(ODataQueryOptions<WorkflowExecutionSummaryDto> queryOptions)
    {
        _ = queryOptions.ApplyTo(Array.Empty<WorkflowExecutionSummaryDto>().AsQueryable());
        return Ok();
    }
}

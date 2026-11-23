using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using System.Security.Claims;
using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Common;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Configuration;
using IniaAi.Domain.Entities.DailyAllowances;
using IniaAi.WebApi.Attributes;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Services.ContentRefinement;
using IniaAi.Testing.DependencyInjection.Mocks;
using IniaAi.WebApi.Configuration;
using IniaAi.WebApi.UnitTests.Common;
using IniaAi.WebApi.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IniaAi.WebApi.UnitTests.Filters;

/// <summary>
/// An API host whose probe endpoints carry the daily allowance, for the user and organization each
/// request names in its headers. A send returns once the server has finished the request, give-back
/// included, so a test reads the count only after the filter is done with it. The refine service is
/// the real one, and its model call fails the way a provider fails after it has billed.
/// </summary>
public sealed class DailyAllowanceHost : IAsyncDisposable
{
    public const string EXPLORE_PATH = "allowance-probe/explore";
    public const string REFINE_PATH = "allowance-probe/refine";
    public const string ORGANIZATION_HEADER = TestHeaderAuthentication.ORGANIZATION_HEADER;
    public const string USER_HEADER = TestHeaderAuthentication.USER_HEADER;
    public const string ROLE_HEADER = TestHeaderAuthentication.ROLE_HEADER;
    public const string LISTED_ORIGIN = "https://app.example.com";

    private readonly WebApplication _app;
    private readonly SemaphoreSlim _finished;

    private DailyAllowanceHost(WebApplication app, SemaphoreSlim finished)
    {
        _app = app;
        _finished = finished;
        Client = app.GetTestClient();
    }

    public HttpClient Client { get; }

    public IServiceProvider Services => _app.Services;

    public static async Task<DailyAllowanceHost> StartAsync(Action<IServiceCollection> configure)
    {
        WebApplicationBuilder builder = TestWebApplication.CreateBuilder();
        builder.Configuration.Sources.Clear();
        _ = builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [$"{CorsConfiguration.ALLOWED_ORIGINS_KEY}:0"] = LISTED_ORIGIN });
        _ = builder.Services.AddControllers().AddApplicationPart(typeof(DailyAllowanceHost).Assembly);
        _ = builder.Services.AddConfiguredApiVersioning();
        _ = builder.Services.AddCorsWithPolicy(builder.Configuration);
        _ = builder.Services.AddHttpContextAccessor();
        _ = builder.Services.AddScoped<IRequestModelUse, RequestModelUse>();
        _ = builder.Services.AddSingleton<IChatConnector>(new ScriptedChatConnector(_ => throw new HttpRequestException("The provider closed the stream after billing.")));
        _ = builder.Services.AddSingleton(new TenantModelFamilyResolver(Options.Create(new PlatformSettings()), NullLogger<TenantModelFamilyResolver>.Instance));
        _ = builder.Services.AddTransient<IContentRefinementService, ContentRefinementService>();
        _ = builder.Services.AddScoped<IRequestContext>(provider =>
        {
            HttpRequest request = provider.GetRequiredService<IHttpContextAccessor>().HttpContext!.Request;
            return new RequestContext
            {
                OrganizationId = request.Headers[ORGANIZATION_HEADER].FirstOrDefault(),
                UserId = request.Headers[USER_HEADER].FirstOrDefault(),
            };
        });
        configure(builder.Services);

        var finished = new SemaphoreSlim(0);
        WebApplication app = builder.Build();
        _ = app.Use(async (context, next) =>
        {
            try
            {
                await next();
            }
            finally
            {
                _ = finished.Release();
            }
        });
        _ = app.Use((context, next) =>
        {
            if (context.Request.Headers[ROLE_HEADER].FirstOrDefault() is { } role)
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], ROLE_HEADER));
            }

            return next();
        });
        _ = app.UseRouting();
        _ = app.UseCors(CorsConfiguration.POLICY_NAME);
        _ = app.MapControllers();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return new DailyAllowanceHost(app, finished);
    }

    /// <summary>Sends a probe request and waits until the server has finished it.</summary>
    public async Task<HttpResponseMessage> SendAsync(
        string path,
        string organizationId,
        string userId,
        ProbeOutcome outcome = ProbeOutcome.Succeed,
        bool withBody = true,
        string? role = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{path}?outcome={outcome}");
        request.Headers.Add(ORGANIZATION_HEADER, organizationId);
        request.Headers.Add(USER_HEADER, userId);
        if (role is not null)
        {
            request.Headers.Add(ROLE_HEADER, role);
        }

        request.Headers.Add("Origin", LISTED_ORIGIN);
        request.Content = JsonContent.Create(withBody ? new ProbeRequest { Question = "Where does this number come from?" } : new ProbeRequest());

        try
        {
            HttpResponseMessage response = await Client.SendAsync(request, TestContext.Current.CancellationToken);
            await response.Content.LoadIntoBufferAsync(TestContext.Current.CancellationToken);
            return response;
        }
        finally
        {
            await _finished.WaitAsync(TestContext.Current.CancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
        _finished.Dispose();
    }
}

public enum ProbeOutcome
{
    Succeed,
    NotFound,
    ModelCallThrows,
    Abort,
    NotFoundAfterTheCountIsLost,
}

public sealed class ProbeRequest
{
    [Required]
    public string? Question { get; set; }
}

[ApiController]
[Route("allowance-probe")]
public sealed class DailyAllowanceProbeController : ControllerBase
{
    private readonly IContentRefinementService _refinement;

    public DailyAllowanceProbeController(IContentRefinementService refinement)
    {
        _refinement = refinement;
    }

    [HttpPost("explore")]
    [DailyAllowance(DailyAllowanceKind.Explore)]
    public Task<IActionResult> Explore([FromBody] ProbeRequest request, [FromQuery] ProbeOutcome outcome)
    {
        return AnswerAsync(outcome);
    }

    [HttpPost("refine")]
    [DailyAllowance(DailyAllowanceKind.Refine)]
    public Task<IActionResult> Refine([FromBody] ProbeRequest request, [FromQuery] ProbeOutcome outcome)
    {
        return AnswerAsync(outcome);
    }

    private async Task<IActionResult> AnswerAsync(ProbeOutcome outcome)
    {
        switch (outcome)
        {
            case ProbeOutcome.NotFound:
                return NotFound(new { error = "Workflow execution not found." });
            case ProbeOutcome.ModelCallThrows:
                try
                {
                    _ = await _refinement.RefineAsync("Revenue rose 4%.", "Make it formal.", [], cancellationToken: HttpContext.RequestAborted);
                    return Ok();
                }
                catch (HttpRequestException)
                {
                    return StatusCode(StatusCodes.Status500InternalServerError);
                }

            case ProbeOutcome.Abort:
                HttpContext.Abort();
                return StatusCode(StatusCodes.Status500InternalServerError);
            case ProbeOutcome.NotFoundAfterTheCountIsLost:
                UserDataDbContext db = HttpContext.RequestServices.GetRequiredService<UserDataDbContext>();
                _ = await db.Database.ExecuteSqlRawAsync("DROP TABLE \"DailyAllowanceUses\"", HttpContext.RequestAborted);
                return NotFound(new { error = "Workflow execution not found." });
            default:
                return Ok(new { answer = "From the balance sheet." });
        }
    }
}

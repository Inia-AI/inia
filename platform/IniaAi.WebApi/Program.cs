using Asp.Versioning.ApiExplorer;
using Finbuckle.MultiTenant.AspNetCore.Extensions;
using HealthChecks.UI.Client;
using IniaAi.Application;
using IniaAi.Configuration.Extensions;
using IniaAi.Infrastructure;
using IniaAi.Infrastructure.Hosting;
using IniaAi.Infrastructure.Persistence.Seed;
using IniaAi.Infrastructure.Persistence.Setup;
using IniaAi.WebApi.Configuration;
using IniaAi.WebApi.Extensions;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;

namespace IniaAi.WebApi;

/// <summary>
/// Application entry point.
/// </summary>
public class Program
{
    /// <summary>
    /// Main entry point for the WebApi application.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    public static async Task Main(string[] args)
    {
        CultureBootstrap.PinInvariantCulture();

        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        // Configuration and Logging
        IConfigurationRoot config = await builder.Configuration.SetupConfigurationAsync();
        _ = builder.AddRunIntakeControl();
        _ = builder.Logging.ClearProviders();
        _ = builder.Logging.AddSerilog(Log.Logger);

        // Database
        _ = builder.AddIniaAiDatabases();

        _ = builder.Services
          .AddPresentation(builder)
          .AddApplication()
          .AddInfrastructure(config);

        WebApplication app = builder.Build();
        ConfigureMiddleware(app);
        ConfigureDatabase(app);

        await SystemUserSeeder.SeedForAllOrganizationsAsync(app.Services, app.Configuration);

        app.Run();
    }

    private static void ConfigureMiddleware(WebApplication app)
    {
        // First, so every later middleware and controller sees the client's address and scheme.
        _ = app.UseForwardedHeaders();

        // Before every middleware that can answer by itself, so refusals, preflights and failures carry the headers.
        _ = app.UseSecurityHeaders();

        _ = app.UseExceptionHandler();

        if (app.Environment.ServesApiDocumentation())
        {
            _ = app.UseSwagger();

            _ = app.UseSwaggerUI(
                options =>
                {
                    IReadOnlyList<ApiVersionDescription> descriptions = app.DescribeApiVersions();
                    foreach (string? groupName in descriptions.Select(desc => desc.GroupName))
                    {
                        options.SwaggerEndpoint($"/swagger/{groupName}/swagger.json", groupName);
                    }

                    options.DisplayRequestDuration();
                });
        }

        _ = app.UseCors(CorsConfiguration.POLICY_NAME);

        // Routing must come before endpoint metadata checks in middleware
        _ = app.UseRouting();

        _ = app.UseRateLimiter();

        _ = app.UseMiddleware<Middleware.ApiVersionReportingMiddleware>();

        // Multi-tenant middleware MUST come before authentication
        _ = app.UseMultiTenant();

        // Early tenant rejection - rejects protected endpoints with missing/invalid X-Tenant-Id BEFORE JWT validation
        _ = app.UseMiddleware<Middleware.EarlyTenantRejectionMiddleware>();

        _ = app.UseAuthentication();

        // Populate IRequestContext and validate user has access to the tenant
        _ = app.UseMiddleware<Middleware.TenantUserContextMiddleware>();

        _ = app.UseAuthorization();

        _ = app.MapControllers();

        _ = app.MapReadOnlyHealthChecks("/health");

        //HealthCheck Middleware
        _ = app.MapReadOnlyHealthChecks("/readiness", new HealthCheckOptions()
        {
            Predicate = _ => true,
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        });
    }
    private static void ConfigureDatabase(WebApplication app)
    {
        _ = app.InitializeDatabases();
    }
}

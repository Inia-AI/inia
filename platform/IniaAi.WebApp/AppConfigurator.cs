using IniaAi.Infrastructure.Hosting;
using IniaAi.WebApp.Components.Account;

namespace IniaAi.WebApp;

public class AppConfigurator
{
    private readonly ILogger _logger;

    public AppConfigurator()
    {
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            _ = builder.AddSimpleConsole();
        });

        _logger = loggerFactory.CreateLogger<AppConfigurator>();
    }

    public void SetupBlazorWebApp(WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            _ = app.UseMigrationsEndPoint();
        }
        else
        {
            _ = app.UseExceptionHandler("/Error", createScopeForErrors: true);
            _ = app.UseHsts();
        }

        // An address no page answers has no endpoint, so the server would send an empty 404; the
        // re-executed request renders the app, whose router shows the not-found page.
        _ = app.UseStatusCodePagesWithReExecute(Components.AppRoutes.NOT_FOUND, createScopeForStatusCodePages: true);
        _ = app.UseHttpsRedirection();
        _ = app.MapStaticAssets();
        _ = app.UseAntiforgery();
        _ = app.UseRateLimiter();

        _ = app.MapRazorComponents<Components.App>()
            .AddInteractiveServerRenderMode();

        _ = app.MapAdditionalIdentityEndpoints();

        _ = app.MapReadOnlyHealthChecks("/health");

        _logger.LogInformation("Blazor web app setup complete.");
    }
}

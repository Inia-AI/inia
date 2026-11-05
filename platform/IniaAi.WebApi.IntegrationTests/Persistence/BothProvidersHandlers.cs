using ErrorOr;
using IniaAi.Application;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.Core.Repositories;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceProvider = Microsoft.Extensions.DependencyInjection.ServiceProvider;

namespace IniaAi.WebApi.IntegrationTests.Persistence;

// Runs the real Application handlers over a throwaway UserData database and, for the handlers that
// read Core, a Core fixture, with the repositories and specifications the app itself uses.
internal static class BothProvidersHandlers
{
    /// <param name="configure">Registers what a test adds before the seam defaults, such as a closed module's seam.</param>
    public static ServiceProvider BuildServices(DbContextOptions<UserDataDbContext> userDataOptions, ICoreDatabaseFixture? core = null, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        _ = services.AddSingleton(userDataOptions);
        _ = services.AddScoped<UserDataDbContext>();
        _ = services.AddScoped<IUserDataDbContext>(provider => provider.GetRequiredService<UserDataDbContext>());
        if (core is not null)
        {
            _ = services.AddSingleton(core.Options);
            _ = services.AddScoped<CoreDbContext>();
            _ = services.AddScoped<ICoreDbContext>(provider => provider.GetRequiredService<CoreDbContext>());
            _ = services.AddScoped<ICoreWorkflowTypeRepository, CoreWorkflowTypeRepository>();
            _ = services.AddScoped<ICoreWorkflowRepository, CoreWorkflowRepository>();
        }

        _ = services.AddTestDatabaseServicesWithoutDbContext();
        configure?.Invoke(services);
        _ = services.AddSeamDefaults();
        _ = services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        _ = services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        return services.BuildServiceProvider();
    }

    public static async Task<TResult> HandleAsync<THandler, TResult>(ServiceProvider services, Func<THandler, Task<ErrorOr<TResult>>> handle)
    {
        using IServiceScope scope = services.CreateScope();
        ErrorOr<TResult> result = await handle(ActivatorUtilities.CreateInstance<THandler>(scope.ServiceProvider));
        Assert.False(result.IsError, $"{typeof(THandler).Name}: {string.Join("; ", result.ErrorsOrEmptyList.Select(error => error.Description))}");
        return result.Value;
    }
}

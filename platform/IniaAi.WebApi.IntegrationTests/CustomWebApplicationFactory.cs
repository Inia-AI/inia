using FluentValidation;
using IniaAi.AiConnection.Abstractions;
using IniaAi.Application.Commands.WorkflowExecutions.StartWorkflowExecution;
using IniaAi.Core.Services;
using IniaAi.Core.Services.Abstraction;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection.Mocks;
using IniaAi.WebApi.IntegrationTests.CommandValidators;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Security.Cryptography;

namespace IniaAi.WebApi.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private static readonly string[] _generatedSecrets =
    [
        "Jwt__SecretKey",
        "OpenIddict__ClientSecret",
        "Seeding__SuperAdminPassword",
        "Seeding__CustomerAdminPassword",
    ];

    public CustomWebApplicationFactory()
    {
        // Must be set before host builds — SetupConfigurationAsync reads this directly
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Tests");

        foreach (string name in _generatedSecrets)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))
            {
                Environment.SetEnvironmentVariable(name, Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
            }
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _ = builder.UseEnvironment("Tests");

        _ = builder.ConfigureAppConfiguration((context, config) =>
         {
             _ = config.AddJsonFile("appsettings.Tests.json", optional: false, reloadOnChange: true);
         });

        _ = builder.ConfigureTestServices(services =>
        {
            // Override any dependencies here for testing (mock database, etc.)
            // services.AddScoped<IMyService, MockMyService>();

            //// Remove real DB and replace with in-memory database
            //var userDbContextDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<UserDataDbContext>));
            //if (userDbContextDescriptor is not null)
            //    services.Remove(userDbContextDescriptor);

            //services.AddDbContext<UserDataDbContext>(options =>
            //    options.UseInMemoryDatabase("UserDataDb"));

            //var coreDbContextDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<CoreDbContext>));
            //if (coreDbContextDescriptor is not null)
            //    services.Remove(coreDbContextDescriptor);

            //services.AddDbContext<CoreDbContext>(options =>
            //    options.UseInMemoryDatabase("CoreDb"));

            _ = services.AddTransient<IValidator<StartWorkflowExecutionCommand>, StartWorkflowExecutionCommandValidator_NoFileCheck>();

            _ = services.RemoveAll<IChatConnector>();
            _ = services.AddSingleton<IChatConnector>(ScriptedChatConnector.Answering("<answer>Yes</answer>"));
            _ = services.RemoveAll<IEmbeddingsConnector>();
            _ = services.AddSingleton<IEmbeddingsConnector>(new WordHashEmbeddingsConnector());
            _ = services.AddHttpClient<IExchangeRateService, ExchangeRateService>()
                .ConfigurePrimaryHttpMessageHandler(() => new CannedExchangeRatesHandler());

        });
    }
}

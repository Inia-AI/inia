using IniaAi.WebApi.IntegrationTests.Builders;
using System.Collections.Concurrent;

namespace IniaAi.WebApi.IntegrationTests.Fixtures;

/// <summary>
/// Provides a shared HTTP client, server setup, and clean database for integration tests.
/// Implements <see cref="IAsyncLifetime"/> for proper setup and teardown.
/// </summary>
public class ApiTestFixture : IAsyncLifetime
{
    /// <summary>
    /// Gets the shared <see cref="ClientBuilder"/> for sending HTTP requests.
    /// </summary>
    public ClientBuilder ClientBuilder { get; private set; } = default!;

    private readonly ConcurrentBag<string> _allCreatedCounterpartyIds = [];

    public ConcurrentTestUserPool UserPool { get; private set; } = default!;

    public void RegisterCreatedCounterparty(string id)
    {
        _allCreatedCounterpartyIds.Add(id);
    }

    /// <summary>
    /// Initializes the test fixture before any tests run.
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        ClientBuilder = new ClientBuilder();

        // Optional: run migrations, seed static data, etc.
        await EnsureDatabaseIsReadyAsync();

        UserPool = new ConcurrentTestUserPool(ClientBuilder.CurrentScope.ServiceProvider);

        // Seed users in DB and pool
        await UserPool.SeedAsync(count: 1);
    }

    /// <summary>
    /// Cleans up any resources after all tests have run.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        // Optional: shut down servers, dispose DI containers.
        await ClientBuilder.CleanCounterpartiesByIdsAsync(_allCreatedCounterpartyIds);

        if (UserPool is not null)
        {
            await UserPool.DisposeAsync();
        }
    }

    /// <summary>
    /// Ensures the test database is migrated and ready.
    /// You can add seed data or static types here too.
    /// </summary>
    private static async Task EnsureDatabaseIsReadyAsync()
    {
        // Example: await ClientBuilder.MigrateDatabaseAsync();
        await Task.CompletedTask;
    }
}

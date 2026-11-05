using IniaAi.Application;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace IniaAi.WebApi.UnitTests.Common;

/// <summary>
/// A handler run against the in-memory test database, the way a controller calls it. The JSON it
/// returns is compared with a checked-in fixture, so a projection that drops or changes a member
/// fails here. The in-memory provider translates no SQL: the both-providers integration tests
/// prove the projections on real servers.
/// </summary>
public abstract class FixtureHandlerTests : IDisposable
{
    private const string ID_PROPERTY = "Id";

    private static readonly JsonSerializerOptions _apiJson = BuildApiJson();

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _services;

    protected FixtureHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();
        _ = services.AddScoped<IUserDataDbContext>(provider => provider.GetRequiredService<UserDataDbContext>());
        _ = services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        _ = services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        _ = services.AddSeamDefaults();
        ConfigureServices(services);
        _services = services.BuildServiceProvider();
    }

    protected UserDataDbContext UserData => _services.GetRequiredService<UserDataDbContext>();

    protected CoreDbContext Core => _services.GetRequiredService<CoreDbContext>();

    public void Dispose()
    {
        _services.Dispose();
        GC.SuppressFinalize(this);
    }

    protected abstract string FixtureFolder { get; }

    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    protected TService Service<TService>()
        where TService : notnull
    {
        return _services.GetRequiredService<TService>();
    }

    protected THandler Handler<THandler>()
    {
        return ActivatorUtilities.CreateInstance<THandler>(_services);
    }

    protected TEntity WithId<TEntity>(TEntity entity, string id)
        where TEntity : class
    {
        UserData.Entry(entity).Property(ID_PROPERTY).CurrentValue = id;
        return entity;
    }

    protected void AssertMatchesFixture(object response, string fixtureName)
    {
        string actual = Normalize(JsonSerializer.Serialize(response, _apiJson));
        string path = Path.Combine(RepositoryRoot.Find(), FixtureFolder, fixtureName + ".json");

        if (!File.Exists(path))
        {
            _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
            Assert.Fail($"The fixture {path} did not exist, so this run wrote it. Review it, then run again.");
        }

        Assert.Equal(Normalize(File.ReadAllText(path)), actual);
    }

    private static string Normalize(string json)
    {
        return json.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n";
    }

    private static JsonSerializerOptions BuildApiJson()
    {
        var options = new JsonSerializerOptions { WriteIndented = true, NewLine = "\n" };
        ControllerConfiguration.ConfigureJsonSerializer(options);
        return options;
    }
}

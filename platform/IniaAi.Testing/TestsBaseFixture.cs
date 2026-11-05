using IniaAi.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IniaAi.Testing;

public abstract class TestsBaseFixture<T> : IDisposable
{
    private const string RESULTS_DIRECTORY = "Results";

    private static readonly Lock _lock = new();

    public PlatformSettings PlatformSettings { get; }
    public IOptions<PlatformSettings> PlatformSettingsOptions { get; }

    /// <summary>
    /// Stable database name for the lifetime of this fixture.
    /// Use this when building test service providers to ensure all tests
    /// in the same class share the same in-memory database.
    /// </summary>
    public string DatabaseName { get; } = $"IniaAi-TestDatabase-{Guid.NewGuid()}";

    public TestsBaseFixture()
    {
        lock (_lock)
        {
            SetupOutputDirectory();

#pragma warning disable CS0618 // Type or member is obsolete
            IConfigurationRoot configuration = Configuration.GetConfigurationRoot([]);
            IServiceProvider serviceProvider = Configuration.GetServiceProvider(configuration);
#pragma warning restore CS0618 // Type or member is obsolete
            PlatformSettingsOptions = serviceProvider.GetRequiredService<IOptions<PlatformSettings>>();
            PlatformSettings = PlatformSettingsOptions.Value;
        }
    }

    private static void SetupOutputDirectory()
    {
        if (!Directory.Exists(RESULTS_DIRECTORY))
        {
            _ = Directory.CreateDirectory(RESULTS_DIRECTORY);
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}

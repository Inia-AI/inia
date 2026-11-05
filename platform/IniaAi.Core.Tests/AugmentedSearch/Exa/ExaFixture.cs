using IniaAi.Core.AugmentedSearch.Exa;
using IniaAi.Core.AugmentedSearch.Exa.Interfaces;
using IniaAi.Testing;

namespace IniaAi.Core.Tests.AugmentedSearch.Exa;

public class ExaFixture<T> : TestsBaseFixture<T>
{
    public IExaSearchService GetExaSearchService(ITestOutputHelper testOutputHelper)
    {
        return new ExaSearchService(
            PlatformSettingsOptions,
            TestsLogger.CreateLogger<ExaSearchService>(testOutputHelper));
    }
}

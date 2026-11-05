using IniaAi.Common.Content;
using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using File = IniaAi.Domain.Entities.Documents.File;

namespace IniaAi.Core.Tests.Workflows;

public class WorkflowsFixture<T> : TestsBaseFixture<T>
{
    private Organization? _cachedTestOrganization;
    private IServiceProvider? _serviceProvider;

    public BinaryContent ImageSample { get; }

    public WorkflowsFixture()
    {
        ImageSample = new(
            BinaryData.FromBytes(System.IO.File.ReadAllBytes("TestResources/elephant.jpg")),
            MediaType.ImageJpeg);
    }

    internal Organization GetOrganization()
    {
        if (_serviceProvider is null)
        {
            throw new InvalidOperationException(
                "BuildTestServiceProvider() must be called before GetOrganization()");
        }

        _cachedTestOrganization ??= TestDatabaseServices
            .GetTestOrganization(_serviceProvider);

        return _cachedTestOrganization;
    }

    internal static Func<File, Task<byte[]>> GetFileContentDelegate()
    {
        // For tests that don't actually use file bytes, return empty array
        return async (file) => await Task.FromResult(file.Bytes ?? []);
    }

    internal IServiceProvider BuildTestServiceProvider(
        ITestOutputHelper testOutputHelper,
        Action<IServiceCollection>? overrideServices = null,
        bool withoutClosedModules = false)
    {
        var builder = new TestServiceProviderBuilder(
            PlatformSettings,
            testOutputHelper,
            DatabaseName)
        {
            WithoutClosedModules = withoutClosedModules,
        };
        _serviceProvider = builder.Build(overrideServices);
        return _serviceProvider;
    }
}

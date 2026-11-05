using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Common;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Core.Services;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection;
using NSubstitute;

namespace IniaAi.Core.Tests.Services;

public class DocumentsFixture : TestsBaseFixture<DocumentAnalysisService>
{
    internal IChatConnector GetChatConnector(ITestOutputHelper testOutputHelper)
    {
        return TestChatConnectorFactory.Create(
            PlatformSettings,
            TestsLogger.CreateLogger<IChatConnector>(testOutputHelper));
    }

    internal static IFileStorageManager GetFileStorageManager()
    {
        return Substitute.For<IFileStorageManager>();
    }

    internal TenantModelFamilyResolver GetTenantModelFamilyResolver(ITestOutputHelper testOutputHelper)
    {
        return new TenantModelFamilyResolver(
            PlatformSettingsOptions,
            TestsLogger.CreateLogger<TenantModelFamilyResolver>(testOutputHelper));
    }
}

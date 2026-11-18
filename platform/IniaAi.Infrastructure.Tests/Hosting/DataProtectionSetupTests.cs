using Amazon.Runtime;
using Amazon.SimpleSystemsManagement;
using IniaAi.Infrastructure.Hosting;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IniaAi.Infrastructure.Tests.Hosting;

/// <summary>
/// The API and the admin app share one Data Protection key ring: a key the admin app stores decrypts in the API,
/// and an export link the API signs opens in either. Both take the ring from the same settings, so a deployment
/// that moves the keys to Parameter Store moves them for both.
/// </summary>
public sealed class DataProtectionSetupTests
{
    private const string SSM_REPOSITORY = "SSMXmlRepository";
    private const string KEY_VAULT_ENCRYPTOR = "AzureKeyVaultXmlEncryptor";

    [Fact]
    public void WithTheAwsProvider_TheKeysLiveInParameterStore()
    {
        using ServiceProvider services = Build(new()
        {
            ["DataProtection:Provider"] = "aws",
            ["DataProtection:AwsSsm:ParameterPath"] = "/test/dataprotection",
            ["DataProtection:AwsSsm:KmsKeyId"] = "alias/test-dataprotection",
        });

        Assert.Equal(SSM_REPOSITORY, KeyRepository(services).GetType().Name);
    }

    [Fact]
    public void WithNoProvider_TheKeysLiveInTheDatabase()
    {
        using ServiceProvider services = Build([]);

        _ = Assert.IsType<EntityFrameworkCoreXmlRepository<UserDataDbContext>>(KeyRepository(services));
    }

    [Fact]
    public void WithTheAzureProvider_TheKeysLiveInTheDatabase_EncryptedWithTheKeyVaultKey()
    {
        using ServiceProvider services = Build(new()
        {
            ["DataProtection:Provider"] = "azure",
            ["DataProtection:AzureKeyVault:KeyUri"] = "https://test-vault.vault.azure.net/keys/dataprotection",
        });

        KeyManagementOptions options = services.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
        _ = Assert.IsType<EntityFrameworkCoreXmlRepository<UserDataDbContext>>(options.XmlRepository);
        Assert.Equal(KEY_VAULT_ENCRYPTOR, options.XmlEncryptor?.GetType().Name);
    }

    [Fact]
    public void WithTheAzureProviderAndNoKey_StartupStops()
    {
        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() => Build(new() { ["DataProtection:Provider"] = "azure" }));

        Assert.Contains("DataProtection:AzureKeyVault:KeyUri", refusal.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        ServiceCollection services = new();
        _ = services.AddLogging();
        _ = services.AddSingleton<IAmazonSimpleSystemsManagement>(new AmazonSimpleSystemsManagementClient(
            new BasicAWSCredentials("test", "test"),
            Amazon.RegionEndpoint.EUWest2));
        _ = services.AddIniaAiDataProtection(configuration);
        return services.BuildServiceProvider();
    }

    private static IXmlRepository KeyRepository(ServiceProvider services)
    {
        return services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository!;
    }
}

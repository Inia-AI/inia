using IniaAi.Configuration.Extensions;
using IniaAi.Configuration.Platform;
using Microsoft.Extensions.Configuration;

namespace IniaAi.Configuration.Tests.Extensions;

public class ConfigurationBuilderExtensionsTests
{
    public ConfigurationBuilderExtensionsTests()
    {
        // Ensure we're not running in AWS or Azure mode unless a test opts in
        Environment.SetEnvironmentVariable(CloudHost.FORCE_AWS_MODE, "false");
        Environment.SetEnvironmentVariable(CloudHost.FORCE_AZURE_MODE, "false");
        Environment.SetEnvironmentVariable("AZURE_KEY_VAULT_URI", null);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("PlatformSettings__IniaAi__ShouldBeOverriddenByEnvVars", "ValueFromEnvVars");
    }

    [Fact]
    public async Task SetupConfigurationAsync_InProductionNotInAws_ShouldOnlyAddAppsettingsAndEnvironmentVariables()
    {
        // Arrange
        var builder = new ConfigurationBuilder();
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
        Environment.SetEnvironmentVariable(CloudHost.FORCE_AWS_MODE, "false");

        // Act
        IConfigurationRoot configuration = await builder.SetupConfigurationAsync(TestContext.Current.CancellationToken);

        // Assert
        string? test = configuration.GetValue<string>("PlatformSettings:IniaAi:Test");
        string? shouldBeOverriddenByEnvVars = configuration.GetValue<string>("PlatformSettings:IniaAi:ShouldBeOverriddenByEnvVars");
        string? shouldBeOverriddenByAppsettingsAws = configuration.GetValue<string>("PlatformSettings:IniaAi:ShouldBeOverriddenByAppsettingsAws");
        string? shouldBeOverriddenByAppsettingsDev = configuration.GetValue<string>("PlatformSettings:IniaAi:ShouldBeOverriddenByAppsettingsDev");
        Assert.NotNull(test);
        Assert.Equal("ValueFromAppsettings", test);
        Assert.Equal("ValueFromAppsettings", shouldBeOverriddenByAppsettingsAws);
        Assert.Equal("ValueFromAppsettings", shouldBeOverriddenByAppsettingsDev);
        Assert.Equal("ValueFromEnvVars", shouldBeOverriddenByEnvVars);
    }

    [Fact]
    public async Task SetupConfigurationAsync_InAzureModeWithoutKeyVaultUri_ShouldAddAzureAppsettingsAndSkipKeyVault()
    {
        // Arrange
        var builder = new ConfigurationBuilder();
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
        Environment.SetEnvironmentVariable(CloudHost.FORCE_AZURE_MODE, "true");

        // Act
        IConfigurationRoot configuration = await builder.SetupConfigurationAsync(TestContext.Current.CancellationToken);

        // Assert
        string? shouldBeOverriddenByAppsettingsAzure = configuration.GetValue<string>("PlatformSettings:IniaAi:ShouldBeOverriddenByAppsettingsAzure");
        string? shouldBeOverriddenByAppsettingsAws = configuration.GetValue<string>("PlatformSettings:IniaAi:ShouldBeOverriddenByAppsettingsAws");
        string? shouldBeOverriddenByEnvVars = configuration.GetValue<string>("PlatformSettings:IniaAi:ShouldBeOverriddenByEnvVars");
        Assert.Equal("ValueFromAppsettingsAzure", shouldBeOverriddenByAppsettingsAzure);
        Assert.Equal("ValueFromAppsettings", shouldBeOverriddenByAppsettingsAws);
        Assert.Equal("ValueFromEnvVars", shouldBeOverriddenByEnvVars);
    }

    [Fact]
    public async Task SetupConfigurationAsync_InAzureModeWithMalformedKeyVaultUri_ThrowsNamingTheVariable()
    {
        // Arrange
        var builder = new ConfigurationBuilder();
        Environment.SetEnvironmentVariable(CloudHost.FORCE_AZURE_MODE, "true");
        Environment.SetEnvironmentVariable("AZURE_KEY_VAULT_URI", "iniaai-kv.vault.azure.net");

        // Act & Assert
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => builder.SetupConfigurationAsync(TestContext.Current.CancellationToken));
        Assert.Contains("AZURE_KEY_VAULT_URI", exception.Message);
    }

    [Fact]
    public async Task SetupConfigurationAsync_WithBothCloudModesForced_ThrowsNamingBothVariables()
    {
        // Arrange
        var builder = new ConfigurationBuilder();
        Environment.SetEnvironmentVariable(CloudHost.FORCE_AWS_MODE, "true");
        Environment.SetEnvironmentVariable(CloudHost.FORCE_AZURE_MODE, "true");

        // Act & Assert
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => builder.SetupConfigurationAsync(TestContext.Current.CancellationToken));
        Assert.Contains(CloudHost.FORCE_AWS_MODE, exception.Message);
        Assert.Contains(CloudHost.FORCE_AZURE_MODE, exception.Message);
    }

    [Fact]
    public async Task SetupConfigurationAsync_WithSettingsDirectories_ReadsEachFileAsASettingBelowTheEnvironmentVariables()
    {
        // Arrange
        string first = Directory.CreateTempSubdirectory().FullName;
        string second = Directory.CreateTempSubdirectory().FullName;
        await File.WriteAllTextAsync(Path.Combine(first, "PlatformSettings__IniaAi__FromFirst"), "ValueFromFirst", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(first, "PlatformSettings__IniaAi__ShouldBeOverriddenByEnvVars"), "ValueFromFile", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(second, "PlatformSettings__IniaAi__FromSecond"), "ValueFromSecond", TestContext.Current.CancellationToken);
        Environment.SetEnvironmentVariable(ConfigurationBuilderExtensions.SETTINGS_DIRECTORIES, first + Path.PathSeparator + second);
        var builder = new ConfigurationBuilder();

        try
        {
            // Act
            IConfigurationRoot configuration = await builder.SetupConfigurationAsync(TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal("ValueFromFirst", configuration["PlatformSettings:IniaAi:FromFirst"]);
            Assert.Equal("ValueFromSecond", configuration["PlatformSettings:IniaAi:FromSecond"]);
            Assert.Equal("ValueFromEnvVars", configuration["PlatformSettings:IniaAi:ShouldBeOverriddenByEnvVars"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConfigurationBuilderExtensions.SETTINGS_DIRECTORIES, null);
            Directory.Delete(first, recursive: true);
            Directory.Delete(second, recursive: true);
        }
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task SetupConfigurationAsync_WithASettingsFileEndingInANewline_ReadsTheValueWithoutIt(string newline)
    {
        // Arrange
        string directory = Directory.CreateTempSubdirectory().FullName;
        await File.WriteAllTextAsync(Path.Combine(directory, "PlatformSettings__IniaAi__WithValue"), "Value" + newline, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory, "PlatformSettings__IniaAi__Unset"), newline, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory, "PlatformSettings__IniaAi__TwoLines"), "First" + newline + "Second" + newline, TestContext.Current.CancellationToken);
        Environment.SetEnvironmentVariable(ConfigurationBuilderExtensions.SETTINGS_DIRECTORIES, directory);
        var builder = new ConfigurationBuilder();

        try
        {
            // Act
            IConfigurationRoot configuration = await builder.SetupConfigurationAsync(TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal("Value", configuration["PlatformSettings:IniaAi:WithValue"]);
            Assert.Equal(string.Empty, configuration["PlatformSettings:IniaAi:Unset"]);
            Assert.Equal("First" + newline + "Second", configuration["PlatformSettings:IniaAi:TwoLines"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConfigurationBuilderExtensions.SETTINGS_DIRECTORIES, null);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SetupConfigurationAsync_WithAMissingSettingsDirectory_ThrowsNamingTheDirectory()
    {
        // Arrange
        string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(ConfigurationBuilderExtensions.SETTINGS_DIRECTORIES, missing);
        var builder = new ConfigurationBuilder();

        try
        {
            // Act & Assert
            DirectoryNotFoundException exception = await Assert.ThrowsAsync<DirectoryNotFoundException>(
                () => builder.SetupConfigurationAsync(TestContext.Current.CancellationToken));
            Assert.Contains(missing, exception.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConfigurationBuilderExtensions.SETTINGS_DIRECTORIES, null);
        }
    }

    [Fact]
    public async Task SetupConfigurationAsync_InDevelopmentNotInAws_ShouldAddAppsettingsDevAndEnvironmentVariables()
    {
        // Arrange
        var builder = new ConfigurationBuilder();
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable(CloudHost.FORCE_AWS_MODE, "false");

        // Act
        IConfigurationRoot configuration = await builder.SetupConfigurationAsync(TestContext.Current.CancellationToken);

        // Assert
        string? test = configuration.GetValue<string>("PlatformSettings:IniaAi:Test");
        string? shouldBeOverriddenByEnvVars = configuration.GetValue<string>("PlatformSettings:IniaAi:ShouldBeOverriddenByEnvVars");
        string? shouldBeOverriddenByAppsettingsAws = configuration.GetValue<string>("PlatformSettings:IniaAi:ShouldBeOverriddenByAppsettingsAws");
        string? shouldBeOverriddenByAppsettingsDev = configuration.GetValue<string>("PlatformSettings:IniaAi:ShouldBeOverriddenByAppsettingsDev");
        Assert.NotNull(test);
        Assert.Equal("ValueFromAppsettingsDev", test);
        Assert.Equal("ValueFromAppsettings", shouldBeOverriddenByAppsettingsAws);
        Assert.Equal("ValueFromAppsettingsDev", shouldBeOverriddenByAppsettingsDev);
        Assert.Equal("ValueFromEnvVars", shouldBeOverriddenByEnvVars);
    }
}

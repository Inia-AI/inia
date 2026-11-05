using IniaAi.Configuration.Data;
using IniaAi.Configuration.Platform;

namespace IniaAi.Configuration.Tests;

public class PlatformSettingsTests
{
    [Fact]
    public void DeploymentMode_UsesModelGateway_IsTheGateway()
    {
        Assert.Equal(DeploymentMode.Gateway, new PlatformSettings { UsesModelGateway = true }.DeploymentMode);
    }

    [Fact]
    public void UserDataDbProvider_ValidValue_SetValue()
    {
        // Arrange
        var settings = new PlatformSettings();
        string validDbProvider = DatabaseProviders.MSSQL;

        // Act
        settings.UserDataDbProvider = validDbProvider;

        // Assert
        Assert.Equal(validDbProvider, settings.UserDataDbProvider);
    }

    [Fact]
    public void UserDataDbProvider_InvalidValue_ThrowsArgumentException()
    {
        // Arrange
        var settings = new PlatformSettings();
        string invalidDbProvider = "invalid";

        // Act & Assert
        _ = Assert.Throws<ArgumentException>(() => settings.UserDataDbProvider = invalidDbProvider);
    }

    [Fact]
    public void CoreDbProvider_ValidValue_SetValue()
    {
        // Arrange
        var settings = new PlatformSettings();
        string validDbProvider = DatabaseProviders.POSTGRE_SQL;

        // Act
        settings.CoreDbProvider = validDbProvider;

        // Assert
        Assert.Equal(validDbProvider, settings.CoreDbProvider);
    }

    [Fact]
    public void CoreDbProvider_InvalidValue_ThrowsArgumentException()
    {
        // Arrange
        var settings = new PlatformSettings();
        string invalidDbProvider = "invalid";

        // Act & Assert
        _ = Assert.Throws<ArgumentException>(() => settings.CoreDbProvider = invalidDbProvider);
    }
}

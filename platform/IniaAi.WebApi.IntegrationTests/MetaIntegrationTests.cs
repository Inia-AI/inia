using IniaAi.WebApi.Contracts.Responses.Meta;
using IniaAi.WebApi.IntegrationTests.Builders;

namespace IniaAi.WebApi.IntegrationTests;

public class MetaIntegrationTests
{

    private readonly ClientBuilder _apiBuilder;

    public MetaIntegrationTests()
    {
        _apiBuilder = new ClientBuilder(); // Use builder to get HttpClient
    }

    #region Version Controller Test

    [Fact]
    public async Task GetVersion_ShouldReturnVersionInformation()
    {
        // Arrange

        // Act
        _ = await _apiBuilder.GetVersion();

        // Assert

        GetMetaVersionResponse? result = _apiBuilder.GetMetaVersionResponse;

        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.Version));
        Assert.Equal(ApplicationVersion.Version, result.Version);
        Assert.Equal(ApplicationVersion.InformationalVersion, result.VersionInformation);
    }

    #endregion Version Controller Test

    [Fact]
    public async Task GetHealthchecks_ShouldReturnHealtchecks()
    {
        // Arrange

        // Act
        _ = await _apiBuilder.GetHealthchecks();

        // Assert

    }
}

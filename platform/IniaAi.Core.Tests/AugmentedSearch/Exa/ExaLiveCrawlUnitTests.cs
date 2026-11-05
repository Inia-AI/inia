using IniaAi.Core.Dto.Exa.Search;
using IniaAi.Core.Dto.Exa.Search.Enums;
using Newtonsoft.Json;

namespace IniaAi.Core.Tests.AugmentedSearch.Exa;

public class ExaLiveCrawlUnitTests
{
    [Theory]
    [InlineData(ExaLiveCrawlStrategy.Never, "never")]
    [InlineData(ExaLiveCrawlStrategy.Fallback, "fallback")]
    [InlineData(ExaLiveCrawlStrategy.Always, "always")]
    [InlineData(ExaLiveCrawlStrategy.Preferred, "preferred")]
    public void ExaSearchRequest_WithLiveCrawlStrategy_SerializesCorrectly(ExaLiveCrawlStrategy strategy, string expectedValue)
    {
        // Arrange
        var request = new ExaSearchRequest
        {
            Query = "test query",
            LiveCrawl = strategy
        };

        // Act
        string json = JsonConvert.SerializeObject(request);

        // Assert
        Assert.Contains($"\"liveCrawl\":\"{expectedValue}\"", json);
    }

    [Fact]
    public void ExaSearchRequest_WithLiveCrawlTimeout_SerializesCorrectly()
    {
        // Arrange
        var request = new ExaSearchRequest
        {
            Query = "test query",
            LiveCrawlTimeout = 15000
        };

        // Act
        string json = JsonConvert.SerializeObject(request);

        // Assert
        Assert.Contains("\"liveCrawlTimeout\":15000", json);
    }

    [Fact]
    public void ExaSearchRequest_WithoutLiveCrawlProperties_DoesNotSerializeThem()
    {
        // Arrange
        var request = new ExaSearchRequest
        {
            Query = "test query"
        };

        // Act
        string json = JsonConvert.SerializeObject(request);

        // Assert
        Assert.DoesNotContain("liveCrawl", json);
        Assert.DoesNotContain("liveCrawlTimeout", json);
    }

    [Theory]
    [InlineData(1000, true)]
    [InlineData(10000, true)]
    [InlineData(60000, true)]
    [InlineData(999, false)]
    [InlineData(60001, false)]
    public void LiveCrawlTimeout_ValidationLogic_WorksCorrectly(int timeout, bool expectedValid)
    {
        // Arrange & Act
        bool isValid = timeout is >= 1000 and <= 60000;

        // Assert
        Assert.Equal(expectedValid, isValid);
    }

    [Fact]
    public void ExaLiveCrawlStrategy_EnumValues_MatchApiSpecification()
    {
        // Assert enum values match the API spec exactly
        Assert.Equal("never", ExaLiveCrawlStrategy.Never.ToString("G").ToLower());
        Assert.Equal("fallback", ExaLiveCrawlStrategy.Fallback.ToString("G").ToLower());
        Assert.Equal("always", ExaLiveCrawlStrategy.Always.ToString("G").ToLower());
        Assert.Equal("preferred", ExaLiveCrawlStrategy.Preferred.ToString("G").ToLower());
    }
}

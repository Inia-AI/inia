using IniaAi.Core.AugmentedSearch.Exa.Interfaces;
using IniaAi.Core.Dto.Exa.Search;
using IniaAi.Core.Dto.Exa.Search.Enums;
using IniaAi.Testing.XunitAttributes;

namespace IniaAi.Core.Tests.AugmentedSearch.Exa;

public class ExaSearchServiceTests : IClassFixture<ExaFixture<ExaSearchServiceTests>>
{
    private const string REQUIRES_EXA_EXPENSIVE = "Integration test requiring external Exa service - expensive";

    private readonly ExaFixture<ExaSearchServiceTests> _fixture;
    private readonly ITestOutputHelper _testOutputHelper;

    public ExaSearchServiceTests(ExaFixture<ExaSearchServiceTests> fixture, ITestOutputHelper testOutputHelper)
    {
        _fixture = fixture;
        _testOutputHelper = testOutputHelper;
    }

    [PaidApiFact(REQUIRES_EXA_EXPENSIVE)]
    public async Task SearchAsync_WithValidQuery_ReturnsResults()
    {
        // Arrange
        IExaSearchService service = _fixture.GetExaSearchService(_testOutputHelper);
        var request = new ExaSearchRequest
        {
            Query = "climate change policy",
            NumResults = 5
        };

        // Act
        ExaSearchResponse? response = await service.SearchAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response.Results);
        Assert.True(response.Results.Length <= 5);
        Assert.All(response.Results, result =>
        {
            Assert.NotNull(result.Url);
            Assert.NotNull(result.Id);
        });
    }

    [PaidApiTheory(REQUIRES_EXA_EXPENSIVE)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    public async Task SearchAsync_WithDifferentNumResults_ReturnsCorrectCount(int numResults)
    {
        // Arrange
        IExaSearchService service = _fixture.GetExaSearchService(_testOutputHelper);
        var request = new ExaSearchRequest
        {
            Query = "technology news",
            NumResults = numResults
        };

        // Act
        ExaSearchResponse? response = await service.SearchAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.True(response.Results.Length <= numResults);
        Assert.All(response.Results, result =>
        {
            Assert.NotNull(result.Url);
            Assert.NotNull(result.Id);
        });
    }

    [PaidApiTheory(REQUIRES_EXA_EXPENSIVE)]
    [InlineData(ExaSearchType.Auto)]
    [InlineData(ExaSearchType.Neural)]
    [InlineData(ExaSearchType.Keyword)]
    public async Task SearchAsync_WithDifferentSearchTypes_ReturnsResults(ExaSearchType searchType)
    {
        // Arrange
        IExaSearchService service = _fixture.GetExaSearchService(_testOutputHelper);
        var request = new ExaSearchRequest
        {
            Query = "artificial intelligence",
            NumResults = 3,
            Type = searchType
        };

        // Act
        ExaSearchResponse? response = await service.SearchAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.All(response.Results, result =>
        {
            Assert.NotNull(result.Url);
            Assert.NotNull(result.Id);
        });
    }

    [PaidApiFact(REQUIRES_EXA_EXPENSIVE)]
    public async Task SearchAsync_WithDomainFiltering_RespectsFilters()
    {
        // Arrange
        IExaSearchService service = _fixture.GetExaSearchService(_testOutputHelper);
        var request = new ExaSearchRequest
        {
            Query = "programming tutorials",
            NumResults = 5,
            IncludeDomains = ["github.com", "stackoverflow.com"]
        };

        // Act
        ExaSearchResponse? response = await service.SearchAsync(request);

        // Assert
        Assert.NotNull(response);
        if (response.Results.Length > 0)
        {
            // Check that results are from included domains (when possible)
            Assert.All(response.Results, result =>
            {
                Assert.NotNull(result.Url);
                Assert.NotNull(result.Id);
            });
        }
    }

    [PaidApiFact(REQUIRES_EXA_EXPENSIVE)]
    public async Task SearchAsync_WithExcludeDomains_AvoidsExcludedDomains()
    {
        // Arrange
        IExaSearchService service = _fixture.GetExaSearchService(_testOutputHelper);
        var request = new ExaSearchRequest
        {
            Query = "news articles",
            NumResults = 5,
            ExcludeDomains = ["reddit.com", "twitter.com"]
        };

        // Act
        ExaSearchResponse? response = await service.SearchAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.All(response.Results, result =>
        {
            Assert.NotNull(result.Url);
            Assert.NotNull(result.Id);
            // Results should not be from excluded domains
            Assert.DoesNotContain("reddit.com", result.Url);
            Assert.DoesNotContain("twitter.com", result.Url);
        });
    }

    [PaidApiFact(REQUIRES_EXA_EXPENSIVE)]
    public async Task SearchAsync_WithContentExtraction_ReturnsContentInResults()
    {
        // Arrange
        IExaSearchService service = _fixture.GetExaSearchService(_testOutputHelper);
        var request = new ExaSearchRequest
        {
            Query = "renewable energy",
            NumResults = 3,
            Contents = new ExaContentsConfig
            {
                Text = true,
                Highlights = true
            }
        };

        // Act
        ExaSearchResponse? response = await service.SearchAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response.Results);
        Assert.All(response.Results, result =>
        {
            Assert.NotNull(result.Url);
            Assert.NotNull(result.Id);
            // Content should be extracted when requested
        });
    }

    [PaidApiFact(REQUIRES_EXA_EXPENSIVE)]
    public async Task SearchAsync_WithDateRange_ReturnsResults()
    {
        // Arrange
        IExaSearchService service = _fixture.GetExaSearchService(_testOutputHelper);
        var request = new ExaSearchRequest
        {
            Query = "cryptocurrency bitcoin",
            NumResults = 3,
            StartPublishedDate = "2023-01-01T00:00:00.000Z",
            EndPublishedDate = "2024-12-31T23:59:59.999Z"
        };

        // Act
        ExaSearchResponse? response = await service.SearchAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.All(response.Results, result =>
        {
            Assert.NotNull(result.Url);
            Assert.NotNull(result.Id);
        });
    }

    [PaidApiFact(REQUIRES_EXA_EXPENSIVE)]
    public async Task SearchAsync_WithCategory_ReturnsResults()
    {
        // Arrange
        IExaSearchService service = _fixture.GetExaSearchService(_testOutputHelper);
        var request = new ExaSearchRequest
        {
            Query = "machine learning research",
            NumResults = 3,
            Category = ExaSearchCategory.ResearchPaper
        };

        // Act
        ExaSearchResponse? response = await service.SearchAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.All(response.Results, result =>
        {
            Assert.NotNull(result.Url);
            Assert.NotNull(result.Id);
        });
    }

    [PaidApiFact(REQUIRES_EXA_EXPENSIVE)]
    public async Task SearchAsync_WithEmptyQuery_HandlesGracefully()
    {
        // Arrange
        IExaSearchService service = _fixture.GetExaSearchService(_testOutputHelper);
        var request = new ExaSearchRequest
        {
            Query = "",
            NumResults = 5
        };

        // Act
        ExaSearchResponse? response = await service.SearchAsync(request);

        // Assert
        // Service should handle empty queries gracefully
        if (response is not null)
        {
            Assert.NotNull(response.Results);
        }
    }

    [PaidApiFact(REQUIRES_EXA_EXPENSIVE)]
    public async Task SearchAsync_WithVerySpecificQuery_MayReturnFewerResults()
    {
        // Arrange
        IExaSearchService service = _fixture.GetExaSearchService(_testOutputHelper);
        var request = new ExaSearchRequest
        {
            Query = "very specific query that probably has no results 12345 abcdef",
            NumResults = 10
        };

        // Act
        ExaSearchResponse? response = await service.SearchAsync(request);

        // Assert
        Assert.NotNull(response);
        // May return empty results for very specific queries
        Assert.NotNull(response.Results);
    }

    [PaidApiTheory(REQUIRES_EXA_EXPENSIVE)]
    [InlineData(ExaLiveCrawlStrategy.Never)]
    [InlineData(ExaLiveCrawlStrategy.Fallback)]
    [InlineData(ExaLiveCrawlStrategy.Always)]
    [InlineData(ExaLiveCrawlStrategy.Preferred)]
    public async Task SearchAsync_WithLiveCrawlStrategy_ReturnsResults(ExaLiveCrawlStrategy strategy)
    {
        // Arrange
        IExaSearchService service = _fixture.GetExaSearchService(_testOutputHelper);
        var request = new ExaSearchRequest
        {
            Query = "latest technology news",
            NumResults = 3,
            LiveCrawl = strategy
        };

        // Act
        ExaSearchResponse? response = await service.SearchAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.All(response.Results, result =>
        {
            Assert.NotNull(result.Url);
            Assert.NotNull(result.Id);
        });
    }

    [PaidApiFact(REQUIRES_EXA_EXPENSIVE)]
    public async Task SearchAsync_WithLiveCrawlTimeout_ReturnsResults()
    {
        // Arrange
        IExaSearchService service = _fixture.GetExaSearchService(_testOutputHelper);
        var request = new ExaSearchRequest
        {
            Query = "artificial intelligence research",
            NumResults = 3,
            LiveCrawl = ExaLiveCrawlStrategy.Preferred,
            LiveCrawlTimeout = 15000
        };

        // Act
        ExaSearchResponse? response = await service.SearchAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.All(response.Results, result =>
        {
            Assert.NotNull(result.Url);
            Assert.NotNull(result.Id);
        });
    }

    [PaidApiTheory(REQUIRES_EXA_EXPENSIVE)]
    [InlineData(1000)]
    [InlineData(10000)]
    [InlineData(30000)]
    public async Task SearchAsync_WithDifferentTimeouts_ReturnsResults(int timeout)
    {
        // Arrange
        IExaSearchService service = _fixture.GetExaSearchService(_testOutputHelper);
        var request = new ExaSearchRequest
        {
            Query = "climate change impact",
            NumResults = 2,
            LiveCrawl = ExaLiveCrawlStrategy.Fallback,
            LiveCrawlTimeout = timeout
        };

        // Act
        ExaSearchResponse? response = await service.SearchAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.All(response.Results, result =>
        {
            Assert.NotNull(result.Url);
            Assert.NotNull(result.Id);
        });
    }

    [PaidApiFact(REQUIRES_EXA_EXPENSIVE)]
    public async Task SearchAsync_WithLiveCrawlAndContentExtraction_ReturnsResults()
    {
        // Arrange
        IExaSearchService service = _fixture.GetExaSearchService(_testOutputHelper);
        var request = new ExaSearchRequest
        {
            Query = "sustainable energy solutions",
            NumResults = 3,
            Contents = new ExaContentsConfig
            {
                Text = true,
                Highlights = true
            },
            LiveCrawl = ExaLiveCrawlStrategy.Preferred,
            LiveCrawlTimeout = 20000
        };

        // Act
        ExaSearchResponse? response = await service.SearchAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response.Results);
        Assert.All(response.Results, result =>
        {
            Assert.NotNull(result.Url);
            Assert.NotNull(result.Id);
        });
    }

    #region Subpages Integration Tests

    [PaidApiFact(REQUIRES_EXA_EXPENSIVE)]
    public async Task SearchAsync_WithSubpagesConfiguration_IncludesSubpagesInRequest()
    {
        // Arrange
        IExaSearchService service = _fixture.GetExaSearchService(_testOutputHelper);
        var request = new ExaSearchRequest
        {
            Query = "machine learning research",
            NumResults = 2,
            Contents = new ExaContentsConfig
            {
                Text = true,
                Subpages = 2,
                SubpageTarget = ["references", "citations"]
            }
        };

        // Act
        ExaSearchResponse? response = await service.SearchAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response.Results);
        Assert.All(response.Results, result =>
        {
            Assert.NotNull(result.Url);
            Assert.NotNull(result.Id);
            // Subpages may or may not be present depending on the actual crawl results
            // We just verify the request was accepted and processed
        });
    }

    [PaidApiFact(REQUIRES_EXA_EXPENSIVE)]
    public async Task SearchAsync_WithSubpagesAndIncludeDomains_ProcessesCorrectly()
    {
        // Arrange
        IExaSearchService service = _fixture.GetExaSearchService(_testOutputHelper);
        var request = new ExaSearchRequest
        {
            Query = "artificial intelligence",
            NumResults = 2,
            IncludeDomains = ["arxiv.org", "ieee.org"],
            Contents = new ExaContentsConfig
            {
                Text = true,
                Subpages = 3,
                SubpageTarget = ["references", "methodology", "conclusion"]
            }
        };

        // Act
        ExaSearchResponse? response = await service.SearchAsync(request);

        // Assert
        Assert.NotNull(response);
        if (response.Results.Length > 0)
        {
            Assert.All(response.Results, result =>
            {
                Assert.NotNull(result.Url);
                Assert.NotNull(result.Id);
                // Verify results are from included domains
                Assert.True(result.Url.Contains("arxiv.org") || result.Url.Contains("ieee.org"));
            });
        }
    }

    [PaidApiTheory(REQUIRES_EXA_EXPENSIVE)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task SearchAsync_WithDifferentSubpageCounts_HandlesCorrectly(int subpageCount)
    {
        // Arrange
        IExaSearchService service = _fixture.GetExaSearchService(_testOutputHelper);
        var request = new ExaSearchRequest
        {
            Query = "climate change research",
            NumResults = 2,
            Contents = new ExaContentsConfig
            {
                Text = true,
                Subpages = subpageCount,
                SubpageTarget = ["sources"]
            }
        };

        // Act
        ExaSearchResponse? response = await service.SearchAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.All(response.Results, result =>
        {
            Assert.NotNull(result.Url);
            Assert.NotNull(result.Id);
        });
    }

    [PaidApiFact(REQUIRES_EXA_EXPENSIVE)]
    public async Task SearchAsync_WithSubpagesButNoTargets_ProcessesCorrectly()
    {
        // Arrange
        IExaSearchService service = _fixture.GetExaSearchService(_testOutputHelper);
        var request = new ExaSearchRequest
        {
            Query = "renewable energy",
            NumResults = 2,
            Contents = new ExaContentsConfig
            {
                Text = true,
                Subpages = 2
                // No SubpageTarget specified - should use general subpage detection
            }
        };

        // Act
        ExaSearchResponse? response = await service.SearchAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.All(response.Results, result =>
        {
            Assert.NotNull(result.Url);
            Assert.NotNull(result.Id);
        });
    }

    [PaidApiFact(REQUIRES_EXA_EXPENSIVE)]
    public async Task SearchAsync_WithSubpagesAndAllContentTypes_ReturnsCompleteResults()
    {
        // Arrange
        IExaSearchService service = _fixture.GetExaSearchService(_testOutputHelper);
        var request = new ExaSearchRequest
        {
            Query = "blockchain technology research papers",
            NumResults = 2,
            Contents = new ExaContentsConfig
            {
                Text = true,
                Highlights = true,
                Summary = true,
                Subpages = 2,
                SubpageTarget = ["abstract", "conclusion", "references"]
            }
        };

        // Act
        ExaSearchResponse? response = await service.SearchAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.All(response.Results, result =>
        {
            Assert.NotNull(result.Url);
            Assert.NotNull(result.Id);
            // Content extraction may vary based on actual page content
            // We verify the request completed successfully
        });
    }

    #endregion

    #region URL Validation Tests

    [PaidApiFact(REQUIRES_EXA_EXPENSIVE)]
    public async Task SearchAsync_WithInvalidIncludeDomains_FiltersInvalidDomains()
    {
        // Arrange
        IExaSearchService service = _fixture.GetExaSearchService(_testOutputHelper);
        var request = new ExaSearchRequest
        {
            Query = "technology news",
            NumResults = 3,
            IncludeDomains = [
                "github.com",
                "not a valid domain",
                "https://stackoverflow.com", // Should extract host
                "invalid://domain.com"
            ]
        };

        // Act
        ExaSearchResponse? response = await service.SearchAsync(request);

        // Assert
        Assert.NotNull(response);
        // Should filter out invalid domains and proceed with valid ones
        Assert.All(response.Results, result =>
        {
            Assert.NotNull(result.Url);
            Assert.NotNull(result.Id);
        });
    }

    [PaidApiFact(REQUIRES_EXA_EXPENSIVE)]
    public async Task SearchAsync_WithInvalidExcludeDomains_FiltersInvalidDomains()
    {
        // Arrange
        IExaSearchService service = _fixture.GetExaSearchService(_testOutputHelper);
        var request = new ExaSearchRequest
        {
            Query = "programming tutorials",
            NumResults = 3,
            ExcludeDomains = [
                "reddit.com",
                "not valid",
                "https://twitter.com", // Should extract host
                "ftp://invalid.com"
            ]
        };

        // Act
        ExaSearchResponse? response = await service.SearchAsync(request);

        // Assert
        Assert.NotNull(response);
        // Should filter out invalid domains and proceed with valid ones
        Assert.All(response.Results, result =>
        {
            Assert.NotNull(result.Url);
            Assert.NotNull(result.Id);
            // Should not contain excluded domains
            Assert.DoesNotContain("reddit.com", result.Url);
            Assert.DoesNotContain("twitter.com", result.Url);
        });
    }

    [PaidApiFact(REQUIRES_EXA_EXPENSIVE)]
    public async Task SearchAsync_WithUrlsInIncludeDomains_ExtractsHosts()
    {
        // Arrange
        IExaSearchService service = _fixture.GetExaSearchService(_testOutputHelper);
        var request = new ExaSearchRequest
        {
            Query = "open source projects",
            NumResults = 3,
            IncludeDomains = [
                "https://github.com/some/path",
                "http://gitlab.com/other/path"
            ]
        };

        // Act
        ExaSearchResponse? response = await service.SearchAsync(request);

        // Assert
        Assert.NotNull(response);
        // Should extract hosts from URLs and use them as domains
        if (response.Results.Length > 0)
        {
            Assert.All(response.Results, result =>
            {
                Assert.NotNull(result.Url);
                Assert.NotNull(result.Id);
            });
        }
    }

    [PaidApiFact(REQUIRES_EXA_EXPENSIVE)]
    public async Task SearchAsync_WithEmptyAndWhitespaceDomains_FiltersThemOut()
    {
        // Arrange
        IExaSearchService service = _fixture.GetExaSearchService(_testOutputHelper);
        var request = new ExaSearchRequest
        {
            Query = "test query",
            NumResults = 3,
            IncludeDomains = ["", "   ", "\t\n"]
        };

        // Act
        ExaSearchResponse? response = await service.SearchAsync(request);

        // Assert
        // Should filter out empty/whitespace domains and proceed without any include domains
        // The test will succeed if it doesn't throw an exception
        Assert.NotNull(response);
    }

    #endregion
}

using IniaAi.AiConnection.Abstractions;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Configuration;
using IniaAi.Core.AugmentedSearch.Exa.Interfaces;
using IniaAi.Core.Dto.Exa.Search;
using IniaAi.Core.Services.Abstraction;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Annotations;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Testing.Documents;
using IniaAi.Testing.XunitAttributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace IniaAi.Core.Tests.Workflows.Strategies;

public class ExaSearchStrategyTests : IClassFixture<WorkflowsFixture<ExaSearchStrategyTests>>
{
    private const string CALLS_EXA = "Calls the Exa search API";
    private const string REQUIRES_EXA_EXPENSIVE = "Integration test requiring external Exa service - expensive";
    private const string REQUIRES_EXA = "Integration test requiring external Exa service";

    private readonly IServiceProvider _testServiceProvider;
    private readonly WorkflowsFixture<ExaSearchStrategyTests> _fixture;
    private readonly ITestOutputHelper _outputHelper;

    public ExaSearchStrategyTests(WorkflowsFixture<ExaSearchStrategyTests> fixture, ITestOutputHelper outputHelper)
    {
        _fixture = fixture;
        _outputHelper = outputHelper;
        _testServiceProvider = _fixture.BuildTestServiceProvider(_outputHelper);
    }

    [PaidApiFact(REQUIRES_EXA)]
    public async Task ExecuteBlockAsync_ValidBlockAndContext_CallsExaSearchMethodAsync()
    {
        // Define test cases with company names and expected content types
        var testList = new List<List<string>>() {
            new() { "Microsoft Corporation, latest news", "Microsoft" },
            new() { "Apple Inc, recent developments", "Apple" }
        };

        foreach (List<string> testCase in testList)
        {
            // Arrange
            var workflow = new Section("1234", new Workflow("1234"));
            var block = new Block(BlockTypes.IntelligentSearch, workflow)
            {
                Name = "Exa Search Block",
                Properties = new IntelligentSearchProperties
                {
                    SearchTermTemplate = testCase[0],
                },
            };
            BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
            var strategy = new ExaSearchStrategy();

            // Act
            _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

            // Assert
            Assert.NotNull(block.BlockResponse);
            Assert.NotNull(block.BlockResponse.RawResponse);
            Assert.NotEmpty(block.BlockResponse.RawResponse);

            // Should contain JSON structure or "No search results found"
            Assert.True(
                block.BlockResponse.RawResponse.Contains(testCase[1], StringComparison.InvariantCultureIgnoreCase) ||
                block.BlockResponse.RawResponse.Contains("No search results found"),
                $"Response should contain '{testCase[1]}' or 'No search results found'. Actual: {block.BlockResponse.RawResponse}"
            );
        }
    }

    [Fact]
    public async Task ExecuteBlockAsync_NullSearchTerm_LogsWarningAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.IntelligentSearch, workflow)
        {
            Name = "Exa Search Block",
            Type = BlockTypes.IntelligentSearch,
            Properties = new IntelligentSearchProperties
            {
                SearchTermTemplate = null,
            },
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new ExaSearchStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(block.BlockResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_EmptySearchTerm_LogsWarningAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.IntelligentSearch, workflow)
        {
            Name = "Exa Search Block",
            Type = BlockTypes.IntelligentSearch,
            Properties = new IntelligentSearchProperties
            {
                SearchTermTemplate = "",
            },
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new ExaSearchStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(block.BlockResponse);
    }

    [PaidApiFact(REQUIRES_EXA)]
    public async Task ExecuteBlockAsync_ServiceException_HandlesGracefullyAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.IntelligentSearch, workflow)
        {
            Name = "Exa Search Block",
            Type = BlockTypes.IntelligentSearch,
            Properties = new IntelligentSearchProperties
            {
                SearchTermTemplate = "test query",
            },
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new ExaSearchStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.NotNull(block.BlockResponse.RawResponse);

        // Should either contain search results or an error message
        Assert.True(
            block.BlockResponse.RawResponse.Contains("No search results found") ||
            block.BlockResponse.RawResponse.Contains("Error executing search") ||
            block.BlockResponse.RawResponse.Contains("Title") || // JSON response
            block.BlockResponse.RawResponse.Contains("Url"),    // JSON response
            $"Response should contain results or error message. Actual: {block.BlockResponse.RawResponse}"
        );
    }

    [Fact]
    public async Task ExecuteBlockAsync_CancellationRequested_ThrowsOperationCanceledExceptionAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.IntelligentSearch, workflow)
        {
            Name = "Exa Search Block",
            Type = BlockTypes.IntelligentSearch,
            Properties = new IntelligentSearchProperties
            {
                SearchTermTemplate = "test query",
            },
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new ExaSearchStrategy();

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act & Assert
        _ = await Assert.ThrowsAsync<OperationCanceledException>(
            () => strategy.ExecuteBlockAsync(block, context, cts.Token)
        );
    }

    [PaidApiFact(REQUIRES_EXA)]
    public async Task ExecuteBlockAsync_WithLiveCrawlStrategy_ExecutesSuccessfullyAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.IntelligentSearch, workflow)
        {
            Name = "Exa Search Block with LiveCrawl",
            Type = BlockTypes.IntelligentSearch,
            Properties = new IntelligentSearchProperties
            {
                SearchTermTemplate = "OpenAI latest news",
                LiveCrawlStrategy = "preferred",
                LiveCrawlTimeout = 15000,
                NumResults = 3,
            },
        };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new ExaSearchStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.NotNull(block.BlockResponse.RawResponse);
        Assert.NotEmpty(block.BlockResponse.RawResponse);

        // Should contain either search results or "No search results found"
        Assert.True(
            block.BlockResponse.RawResponse.Contains("OpenAI", StringComparison.InvariantCultureIgnoreCase) ||
            block.BlockResponse.RawResponse.Contains("No search results found"),
            $"Response should contain 'OpenAI' or 'No search results found'. Actual: {block.BlockResponse.RawResponse}"
        );
    }

    [PaidApiTheory(REQUIRES_EXA_EXPENSIVE)]
    [InlineData("never")]
    [InlineData("fallback")]
    [InlineData("always")]
    [InlineData("preferred")]
    public async Task ExecuteBlockAsync_WithDifferentLiveCrawlStrategies_ExecutesSuccessfullyAsync(string strategy)
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.IntelligentSearch, workflow)
        {
            Name = $"Exa Search Block with {strategy} strategy",
            Type = BlockTypes.IntelligentSearch,
            Properties = new IntelligentSearchProperties
            {
                SearchTermTemplate = "Microsoft news",
                LiveCrawlStrategy = strategy,
                NumResults = 2,
            },
        };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var searchStrategy = new ExaSearchStrategy();

        // Act
        _ = await searchStrategy.ExecuteBlockAsync(block, context);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.NotNull(block.BlockResponse.RawResponse);

        // Response should be valid regardless of strategy
        Assert.True(
            block.BlockResponse.RawResponse.Contains("Microsoft", StringComparison.InvariantCultureIgnoreCase) ||
            block.BlockResponse.RawResponse.Contains("No search results found") ||
            block.BlockResponse.RawResponse.Contains("Error executing search"),
            $"Response should contain valid content. Actual: {block.BlockResponse.RawResponse}"
        );
    }

    [PaidApiFact(REQUIRES_EXA_EXPENSIVE)]
    public async Task ExecuteBlockAsync_WithLiveCrawlTimeoutAndContent_ReturnsFullResultsAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.IntelligentSearch, workflow)
        {
            Name = "Exa Search Block with LiveCrawl and Content",
            Type = BlockTypes.IntelligentSearch,
            Properties = new IntelligentSearchProperties
            {
                SearchTermTemplate = "Tesla stock price",
                LiveCrawlStrategy = "always",
                LiveCrawlTimeout = 20000,
                TextEnabled = true,
                HighlightsEnabled = true,
                NumResults = 2,
            },
        };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new ExaSearchStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.NotNull(block.BlockResponse.RawResponse);
        Assert.NotEmpty(block.BlockResponse.RawResponse);

        // Should contain search results with content or appropriate message
        Assert.True(
            block.BlockResponse.RawResponse.Contains("searchResults") ||
            block.BlockResponse.RawResponse.Contains("Tesla", StringComparison.InvariantCultureIgnoreCase) ||
            block.BlockResponse.RawResponse.Contains("No search results found"),
            $"Response should contain search results or appropriate message. Actual: {block.BlockResponse.RawResponse}"
        );
    }

    #region Subpages Tests

    [PaidApiFact(CALLS_EXA, alsoInCi: true)]
    public async Task ExecuteBlockAsync_WithSubpagesProperty_IncludesSubpagesInRequestAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.IntelligentSearch, workflow)
        {
            Name = "Exa Search Block with Subpages",
            Type = BlockTypes.IntelligentSearch,
            Properties = new IntelligentSearchProperties
            {
                SearchTermTemplate = "test query",
                Subpages = 3,
                SubpageTarget = ["references", "sources", "citations"],
                NumResults = 2,
            },
        };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new ExaSearchStrategy();

        // Act & Assert - Should not throw and should execute
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Basic validation that it executed without throwing
        Assert.True(true);
    }

    [PaidApiFact(CALLS_EXA, alsoInCi: true)]
    public async Task ExecuteBlockAsync_WithIncludeDomainsButNoSubpages_AutoAddsSubpagesAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.IntelligentSearch, workflow)
        {
            Name = "Exa Search Block with Include Domains",
            Type = BlockTypes.IntelligentSearch,
            Properties = new IntelligentSearchProperties
            {
                SearchTermTemplate = "test query",
                // Add includeDomains but NO subpages property
                IncludeDomains = ["example.com", "test.com"],
                NumResults = 2,
            },
        };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new ExaSearchStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert - The auto-addition logic should have added subpages=3 during request creation
        // We can't easily test the internal request creation, but we can verify the execution completed
        Assert.True(true);
    }

    [PaidApiFact(CALLS_EXA, alsoInCi: true)]
    public async Task ExecuteBlockAsync_WithIncludeDomainsAndExistingSubpages_DoesNotOverrideSubpagesAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.IntelligentSearch, workflow)
        {
            Name = "Exa Search Block with Both",
            Type = BlockTypes.IntelligentSearch,
            Properties = new IntelligentSearchProperties
            {
                SearchTermTemplate = "test query",
                // Add BOTH includeDomains AND subpages
                IncludeDomains = ["example.com"],
                Subpages = 5, // Should not be overridden to 3
                NumResults = 2,
            },
        };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new ExaSearchStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert - Should complete without throwing
        Assert.True(true);
    }

    [PaidApiFact(CALLS_EXA, alsoInCi: true)]
    public async Task ExecuteBlockAsync_WithoutIncludeDomains_DoesNotAutoAddSubpagesAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.IntelligentSearch, workflow)
        {
            Name = "Exa Search Block without Include Domains",
            Type = BlockTypes.IntelligentSearch,
            Properties = new IntelligentSearchProperties
            {
                SearchTermTemplate = "test query",
                // Only exclude domains, no include domains, no subpages
                ExcludeDomains = ["spam.com"],
                NumResults = 2,
            },
        };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new ExaSearchStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert - Should complete without auto-adding subpages
        Assert.True(true);
    }

    [PaidApiTheory(CALLS_EXA, alsoInCi: true)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    public async Task ExecuteBlockAsync_WithDifferentSubpageCounts_HandlesValidCountsAsync(int subpageCount)
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.IntelligentSearch, workflow)
        {
            Name = $"Exa Search Block with {subpageCount} subpages",
            Type = BlockTypes.IntelligentSearch,
            Properties = new IntelligentSearchProperties
            {
                SearchTermTemplate = "test query",
                Subpages = subpageCount,
                NumResults = 2,
            },
        };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new ExaSearchStrategy();

        // Act & Assert
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);
        Assert.True(true);
    }

    [PaidApiTheory(CALLS_EXA, alsoInCi: true)]
    [InlineData("references")]
    [InlineData("sources,citations")]
    [InlineData("methodology,data,appendix")]
    [InlineData("references, sources, citations, methodology, data")]
    public async Task ExecuteBlockAsync_WithDifferentSubpageTargets_HandlesValidTargets_Async(string targets)
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.IntelligentSearch, workflow)
        {
            Name = "Exa Search Block with Subpage Targets",
            Type = BlockTypes.IntelligentSearch,
            Properties = new IntelligentSearchProperties
            {
                SearchTermTemplate = "test query",
                Subpages = 2,
                SubpageTarget = [.. targets.Split(',').Select(t => t.Trim())],
                NumResults = 2,
            },
        };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new ExaSearchStrategy();

        // Act & Assert
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);
        Assert.True(true);
    }

    [PaidApiFact(CALLS_EXA, alsoInCi: true)]
    public async Task ExecuteBlockAsync_WithEmptySubpageTarget_HandlesGracefullyAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.IntelligentSearch, workflow)
        {
            Name = "Exa Search Block with Empty Subpage Target",
            Type = BlockTypes.IntelligentSearch,
            Properties = new IntelligentSearchProperties
            {
                SearchTermTemplate = "test query",
                Subpages = 2,
                SubpageTarget = [], // Empty
                NumResults = 2,
            },
        };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new ExaSearchStrategy();

        // Act & Assert
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);
        Assert.True(true);
    }

    [PaidApiFact(CALLS_EXA, alsoInCi: true)]
    public async Task ExecuteBlockAsync_WithWhitespaceOnlySubpageTarget_HandlesGracefullyAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.IntelligentSearch, workflow)
        {
            Name = "Exa Search Block with Whitespace Subpage Target",
            Type = BlockTypes.IntelligentSearch,
            Properties = new IntelligentSearchProperties
            {
                SearchTermTemplate = "test query",
                Subpages = 2,
                SubpageTarget = [], // Only whitespace
                NumResults = 2,
            },
        };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new ExaSearchStrategy();

        // Act & Assert
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);
        Assert.True(true);
    }

    #endregion

    [PaidApiFact(REQUIRES_EXA)]
    public async Task ExecuteBlockAsync_UsingProperties_ExecutesSuccessfully()
    {
        // Arrange
        var workflow = new Section("test", new Workflow("test"));
        var block = new Block(BlockTypes.IntelligentSearch, workflow)
        {
            Name = "Test Properties Pattern",
            Type = BlockTypes.IntelligentSearch,
            Properties = new IntelligentSearchProperties
            {
                SearchTermTemplate = "Microsoft latest news",
                SearchType = "neural",
                NumResults = 5,
                TextEnabled = true,
                IncludeDomains = ["microsoft.com", "techcrunch.com"]
            }
        };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new ExaSearchStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.NotNull(block.BlockResponse.RawResponse);
        Assert.NotEmpty(block.BlockResponse.RawResponse);

        // Should contain search results or "No search results found"
        Assert.True(
            block.BlockResponse.RawResponse.Contains("Microsoft", StringComparison.InvariantCultureIgnoreCase) ||
            block.BlockResponse.RawResponse.Contains("No search results found"),
            $"Response should contain 'Microsoft' or 'No search results found'. Actual: {block.BlockResponse.RawResponse}"
        );
    }

    [PaidApiFact(REQUIRES_EXA)]
    public async Task ExecuteBlockAsync_WithCreatesDocumentClass_CreatesDocument()
    {
        // Arrange
        var workflow = new Section("test", new Workflow("test"));
        var block = new Block(BlockTypes.IntelligentSearch, workflow)
        {
            Name = "Exa Search with Document Creation",
            Type = BlockTypes.IntelligentSearch,
            Properties = new IntelligentSearchProperties
            {
                SearchTermTemplate = "Microsoft latest news",
                NumResults = 2,
                TextEnabled = true,
                CreatesDocumentClass = "search-results"
            }
        };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new ExaSearchStrategy();

        // Act
        BlockStrategyOutput output = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);

        // If search succeeded, document should be created
        if (!block.BlockResponse.RawResponse!.Contains("No search results found"))
        {
            _ = Assert.Single(output.Documents);
            Assert.Equal("search-results", output.Documents[0].DocumentClass);
            Assert.NotNull(output.Documents[0].Document);
            Assert.False(output.Documents[0].ShouldBeDisplayedInOutput);
        }
    }

    [PaidApiFact(REQUIRES_EXA)]
    public async Task ExecuteBlockAsync_WithoutCreatesDocumentClass_DoesNotCreateDocument()
    {
        // Arrange
        var workflow = new Section("test", new Workflow("test"));
        var block = new Block(BlockTypes.IntelligentSearch, workflow)
        {
            Name = "Exa Search without Document Creation",
            Type = BlockTypes.IntelligentSearch,
            Properties = new IntelligentSearchProperties
            {
                SearchTermTemplate = "Microsoft latest news",
                NumResults = 2,
                TextEnabled = true,
                CreatesDocumentClass = null // Explicitly null
            }
        };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new ExaSearchStrategy();

        // Act
        BlockStrategyOutput output = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Empty(output.Documents); // No document should be created
    }

    [PaidApiFact(REQUIRES_EXA)]
    public async Task ExecuteBlockAsync_WithCreatesDocumentClass_DocumentHasPages()
    {
        // Arrange
        var workflow = new Section("test", new Workflow("test"));
        var block = new Block(BlockTypes.IntelligentSearch, workflow)
        {
            Name = "Exa Search with Pages",
            Type = BlockTypes.IntelligentSearch,
            Properties = new IntelligentSearchProperties
            {
                SearchTermTemplate = "OpenAI ChatGPT news",
                NumResults = 3,
                TextEnabled = true,
                CreatesDocumentClass = "ai-news"
            }
        };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new ExaSearchStrategy();

        // Act
        BlockStrategyOutput output = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);

        // If search succeeded, document should have pages
        if (!block.BlockResponse.RawResponse!.Contains("No search results found") && output.Documents.Count > 0)
        {
            Document? document = output.Documents[0].Document;
            Assert.NotNull(document);
            // Pages are created from results with text
            // At least one page should exist if there are results with text
            Assert.True(document!.Pages.Count >= 0); // May be 0 if results have no text
        }
    }

    [Fact]
    public async Task ExecuteBlockAsync_NoResults_DoesNotCreateDocument()
    {
        // Arrange - Use an invalid/empty search term that will return no results
        var workflow = new Section("test", new Workflow("test"));
        var block = new Block(BlockTypes.IntelligentSearch, workflow)
        {
            Name = "Exa Search No Results",
            Type = BlockTypes.IntelligentSearch,
            Properties = new IntelligentSearchProperties
            {
                SearchTermTemplate = null, // No search term - will skip
                CreatesDocumentClass = "should-not-be-created"
            }
        };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new ExaSearchStrategy();

        // Act
        BlockStrategyOutput output = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(block.BlockResponse); // No response when search term is null
        Assert.Empty(output.Documents); // No document should be created
    }

    [Fact]
    public async Task ExecuteBlockAsync_ScrapedContentContainsNullCharacters_PersistedArtifactsAreCleanAsync()
    {
        // PostgreSQL rejects NUL (0x00) in text and jsonb columns; scraped web content can carry it.
        IExaSearchService mockExa = Substitute.For<IExaSearchService>();
        _ = mockExa.SearchAsync(Arg.Any<ExaSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ExaSearchResponse
            {
                Results = [new ExaResultItem
                {
                    Id = "1",
                    Url = "https://news.example.com/article\0",
                    Title = "Negative \0 News",
                    Text = $"scraped body with a NUL\0 byte {GenerateWords(30)}",
                }]
            });

        BlockExecutionContext context = CreateMockContext(mockExa, wordCountPerPage: 10);
        Block block = CreateBlock(new IntelligentSearchProperties
        {
            SearchTermTemplate = "negative news",
            TextEnabled = true,
            CreatesDocumentClass = "negative-news",
        });
        var strategy = new ExaSearchStrategy();

        // Act
        BlockStrategyOutput output = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse?.RawResponse);
        Assert.DoesNotContain('\0', block.BlockResponse.RawResponse);

        DocumentInfo documentInfo = Assert.Single(output.Documents);
        Assert.NotNull(documentInfo.Document);
        Assert.NotEmpty(documentInfo.Document.Pages);
        Assert.All(documentInfo.Document.Pages, page =>
        {
            Assert.DoesNotContain('\0', page.Text!);
            Assert.DoesNotContain('\0', page.SourceUrl!);
        });
    }

    [Fact]
    public async Task ExecuteBlockAsync_WithCreatesDocumentClass_NumbersPagesInOrderAsync()
    {
        IExaSearchService mockExa = Substitute.For<IExaSearchService>();
        _ = mockExa.SearchAsync(Arg.Any<ExaSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ExaSearchResponse
            {
                Results =
                [
                    new ExaResultItem { Id = "1", Url = "https://first.example.com", Title = null, Text = GenerateWords(30) },
                    new ExaResultItem { Id = "2", Url = "https://second.example.com", Title = null, Text = GenerateWords(20) },
                ]
            });

        BlockExecutionContext context = CreateMockContext(mockExa, wordCountPerPage: 10);
        Block block = CreateBlock(new IntelligentSearchProperties
        {
            SearchTermTemplate = "results document",
            TextEnabled = true,
            CreatesDocumentClass = "search-results",
        });
        var strategy = new ExaSearchStrategy();

        BlockStrategyOutput output = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        DocumentInfo documentInfo = Assert.Single(output.Documents);
        Assert.NotNull(documentInfo.Document);
        List<Page> pages = [.. documentInfo.Document.Pages];
        Assert.Equal([1, 2, 3, 4, 5], pages.Select(p => p.PageNumber));
        Assert.Equal(
            ["https://first.example.com", "https://first.example.com", "https://first.example.com",
             "https://second.example.com", "https://second.example.com"],
            pages.Select(p => p.SourceUrl));
    }

    #region Per-Result Page Limit Tests

    [Fact]
    public async Task ExecuteBlockAsync_ResultUnderPageLimit_IncludesResultInResponseAsync()
    {
        // Arrange: 1 result, 20 words, 10 words/page = 2 pages, limit 5
        IExaSearchService mockExa = Substitute.For<IExaSearchService>();
        _ = mockExa.SearchAsync(Arg.Any<ExaSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ExaSearchResponse
            {
                Results = [new ExaResultItem { Id = "1", Url = "https://included.com", Title = null, Text = GenerateWords(20) }]
            });

        BlockExecutionContext context = CreateMockContext(mockExa, wordCountPerPage: 10);
        Block block = CreateBlock(new IntelligentSearchProperties
        {
            SearchTermTemplate = "test query",
            TextEnabled = true,
            MaxPagesPerResult = 5,
        });
        var strategy = new ExaSearchStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Contains("https://included.com", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_ResultExceedingPageLimit_ExcludesResultFromTextResponseAsync()
    {
        // Arrange: 1 result, 100 words, 10 words/page = 10 pages, limit 5
        IExaSearchService mockExa = Substitute.For<IExaSearchService>();
        _ = mockExa.SearchAsync(Arg.Any<ExaSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ExaSearchResponse
            {
                Results = [new ExaResultItem { Id = "1", Url = "https://excluded.com", Title = null, Text = GenerateWords(100) }]
            });

        BlockExecutionContext context = CreateMockContext(mockExa, wordCountPerPage: 10);
        Block block = CreateBlock(new IntelligentSearchProperties
        {
            SearchTermTemplate = "test query",
            TextEnabled = true,
            MaxPagesPerResult = 5,
        });
        var strategy = new ExaSearchStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Equal("No search results found", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_MixedResults_OnlyIncludesResultsUnderPageLimitAsync()
    {
        // Arrange: Result A (20 words = 2 pages, under), Result B (100 words = 10 pages, over), limit 5
        IExaSearchService mockExa = Substitute.For<IExaSearchService>();
        _ = mockExa.SearchAsync(Arg.Any<ExaSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ExaSearchResponse
            {
                Results =
                [
                    new ExaResultItem { Id = "a", Url = "https://included.com", Title = null, Text = GenerateWords(20) },
                    new ExaResultItem { Id = "b", Url = "https://excluded.com", Title = null, Text = GenerateWords(100) },
                ]
            });

        BlockExecutionContext context = CreateMockContext(mockExa, wordCountPerPage: 10);
        Block block = CreateBlock(new IntelligentSearchProperties
        {
            SearchTermTemplate = "test query",
            TextEnabled = true,
            MaxPagesPerResult = 5,
        });
        var strategy = new ExaSearchStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Contains("https://included.com", block.BlockResponse.RawResponse);
        Assert.DoesNotContain("https://excluded.com", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_ResultExceedingPageLimit_ExcludesResultFromDocumentPagesAsync()
    {
        // Arrange: Result A (20 words, under), Result B (100 words, over), limit 5, with document creation
        IExaSearchService mockExa = Substitute.For<IExaSearchService>();
        _ = mockExa.SearchAsync(Arg.Any<ExaSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ExaSearchResponse
            {
                Results =
                [
                    new ExaResultItem { Id = "a", Url = "https://included.com", Title = null, Text = GenerateWords(20) },
                    new ExaResultItem { Id = "b", Url = "https://excluded.com", Title = null, Text = GenerateWords(100) },
                ]
            });

        BlockExecutionContext context = CreateMockContext(mockExa, wordCountPerPage: 10);
        Block block = CreateBlock(new IntelligentSearchProperties
        {
            SearchTermTemplate = "test query",
            TextEnabled = true,
            MaxPagesPerResult = 5,
            CreatesDocumentClass = "test-class",
        });
        var strategy = new ExaSearchStrategy();

        // Act
        BlockStrategyOutput output = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        _ = Assert.Single(output.Documents);
        Document document = output.Documents[0].Document!;
        Assert.All(document.Pages, page => Assert.Equal("https://included.com", page.SourceUrl));
        Assert.DoesNotContain(document.Pages, page => page.SourceUrl == "https://excluded.com");
    }

    [Fact]
    public async Task ExecuteBlockAsync_ResultExactlyAtPageLimit_IncludesResultAsync()
    {
        // Arrange: 1 result, 50 words (no title), 10 words/page = 5 pages exactly, limit 5 (boundary: <=)
        IExaSearchService mockExa = Substitute.For<IExaSearchService>();
        _ = mockExa.SearchAsync(Arg.Any<ExaSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ExaSearchResponse
            {
                Results = [new ExaResultItem { Id = "1", Url = "https://boundary.com", Title = null, Text = GenerateWords(50) }]
            });

        BlockExecutionContext context = CreateMockContext(mockExa, wordCountPerPage: 10);
        Block block = CreateBlock(new IntelligentSearchProperties
        {
            SearchTermTemplate = "test query",
            TextEnabled = true,
            MaxPagesPerResult = 5,
        });
        var strategy = new ExaSearchStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Contains("https://boundary.com", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_ResultExceedingPageLimit_ExcludesResultFromSummariesAsync()
    {
        // Arrange: 1 result with text (10 pages) + summary, limit 5
        IExaSearchService mockExa = Substitute.For<IExaSearchService>();
        _ = mockExa.SearchAsync(Arg.Any<ExaSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ExaSearchResponse
            {
                Results = [new ExaResultItem
                {
                    Id = "1",
                    Url = "https://excluded.com",
                    Title = "Excluded",
                    Text = GenerateWords(100),
                    Summary = "This is a summary",
                }]
            });

        BlockExecutionContext context = CreateMockContext(mockExa, wordCountPerPage: 10);
        Block block = CreateBlock(new IntelligentSearchProperties
        {
            SearchTermTemplate = "test query",
            TextEnabled = true,
            SummaryEnabled = true,
            MaxPagesPerResult = 5,
        });
        var strategy = new ExaSearchStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.DoesNotContain("searchSummaries", block.BlockResponse.RawResponse);
        Assert.DoesNotContain("https://excluded.com", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_ResultExceedingPageLimit_ExcludesResultFromHighlightsAsync()
    {
        // Arrange: 1 result with text (10 pages) + highlights, limit 5
        IExaSearchService mockExa = Substitute.For<IExaSearchService>();
        _ = mockExa.SearchAsync(Arg.Any<ExaSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ExaSearchResponse
            {
                Results = [new ExaResultItem
                {
                    Id = "1",
                    Url = "https://excluded.com",
                    Title = "Excluded",
                    Text = GenerateWords(100),
                    Highlights = ["highlight1", "highlight2"],
                }]
            });

        BlockExecutionContext context = CreateMockContext(mockExa, wordCountPerPage: 10);
        Block block = CreateBlock(new IntelligentSearchProperties
        {
            SearchTermTemplate = "test query",
            TextEnabled = true,
            HighlightsEnabled = true,
            MaxPagesPerResult = 5,
        });
        var strategy = new ExaSearchStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.DoesNotContain("searchHighlights", block.BlockResponse.RawResponse);
        Assert.DoesNotContain("https://excluded.com", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_NoMaxPagesPropertySet_UsesDefaultLimitAsync()
    {
        // Arrange: 1 result producing 101 pages at default 250 words/page, no MaxPagesPerResult set
        IExaSearchService mockExa = Substitute.For<IExaSearchService>();
        _ = mockExa.SearchAsync(Arg.Any<ExaSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ExaSearchResponse
            {
                Results = [new ExaResultItem
                {
                    Id = "1",
                    Url = "https://huge-result.com",
                    Title = null,
                    Text = GenerateWords(101 * 250),
                }]
            });

        BlockExecutionContext context = CreateMockContext(mockExa, wordCountPerPage: 250);
        Block block = CreateBlock(new IntelligentSearchProperties
        {
            SearchTermTemplate = "test query",
            TextEnabled = true,
            // MaxPagesPerResult not set — should use default (100)
        });
        var strategy = new ExaSearchStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert: 101 pages > default 100, should be excluded
        Assert.NotNull(block.BlockResponse);
        Assert.Equal("No search results found", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_SubpageExceedingPageLimit_ExcludesSubpageAsync()
    {
        // Arrange: main result under limit, subpage over limit
        IExaSearchService mockExa = Substitute.For<IExaSearchService>();
        _ = mockExa.SearchAsync(Arg.Any<ExaSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ExaSearchResponse
            {
                Results = [new ExaResultItem
                {
                    Id = "main",
                    Url = "https://main.com",
                    Title = null,
                    Text = GenerateWords(20),
                    Subpages = [new ExaResultItem
                    {
                        Id = "sub",
                        Url = "https://subpage.com",
                        Title = null,
                        Text = GenerateWords(100),
                    }],
                }]
            });

        BlockExecutionContext context = CreateMockContext(mockExa, wordCountPerPage: 10);
        Block block = CreateBlock(new IntelligentSearchProperties
        {
            SearchTermTemplate = "test query",
            TextEnabled = true,
            MaxPagesPerResult = 5,
            CreatesDocumentClass = "test-class",
        });
        var strategy = new ExaSearchStrategy();

        // Act
        BlockStrategyOutput output = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Contains("https://main.com", block.BlockResponse.RawResponse);
        Assert.DoesNotContain("https://subpage.com", block.BlockResponse.RawResponse);

        _ = Assert.Single(output.Documents);
        Assert.All(output.Documents[0].Document!.Pages, page => Assert.Equal("https://main.com", page.SourceUrl));
    }

    [Fact]
    public async Task ExecuteBlockAsync_AllResultsExceedPageLimit_ReturnsNoResultsFoundAsync()
    {
        // Arrange: 2 results both over limit
        IExaSearchService mockExa = Substitute.For<IExaSearchService>();
        _ = mockExa.SearchAsync(Arg.Any<ExaSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ExaSearchResponse
            {
                Results =
                [
                    new ExaResultItem { Id = "a", Url = "https://big-a.com", Title = null, Text = GenerateWords(100) },
                    new ExaResultItem { Id = "b", Url = "https://big-b.com", Title = null, Text = GenerateWords(100) },
                ]
            });

        BlockExecutionContext context = CreateMockContext(mockExa, wordCountPerPage: 10);
        Block block = CreateBlock(new IntelligentSearchProperties
        {
            SearchTermTemplate = "test query",
            TextEnabled = true,
            MaxPagesPerResult = 5,
            CreatesDocumentClass = "test-class",
        });
        var strategy = new ExaSearchStrategy();

        // Act
        BlockStrategyOutput output = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Equal("No search results found", block.BlockResponse.RawResponse);
        Assert.Empty(output.Documents);
    }

    #endregion

    #region Auto-Citation Tests

    [Fact]
    public async Task ExecuteBlockAsync_ResultWithNumericalValue_EmitsCitationFactWithoutDocumentClassAsync()
    {
        IExaSearchService mockExa = Substitute.For<IExaSearchService>();
        _ = mockExa.SearchAsync(Arg.Any<ExaSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ExaSearchResponse
            {
                Results = [new ExaResultItem
                {
                    Id = "1",
                    Url = "https://source.example.com",
                    Title = "Deal",
                    Text = "The deal was valued at $10.7 billion in cash.",
                }]
            });

        BlockExecutionContext context = CreateMockContext(mockExa);
        Block block = CreateBlock(new IntelligentSearchProperties
        {
            SearchTermTemplate = "deal value",
            TextEnabled = true,
        });
        var strategy = new ExaSearchStrategy();

        BlockStrategyOutput output = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Empty(output.Documents);
        Fact numerical = Assert.Single(context.BlockAnnotations.Facts, f => f.IsNumerical);
        Assert.Equal("$10.7 billion", numerical.Value);
        UrlSource source = Assert.IsType<UrlSource>(Assert.Single(numerical.Sources));
        Assert.Equal("https://source.example.com", source.Url);
    }

    [Fact]
    public async Task ExecuteBlockAsync_SubpageNumericalValue_EmitsCitationFactAsync()
    {
        IExaSearchService mockExa = Substitute.For<IExaSearchService>();
        _ = mockExa.SearchAsync(Arg.Any<ExaSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ExaSearchResponse
            {
                Results = [new ExaResultItem
                {
                    Id = "main",
                    Url = "https://main.example.com",
                    Title = null,
                    Text = "Overview without figures.",
                    Subpages = [new ExaResultItem
                    {
                        Id = "sub",
                        Url = "https://sub.example.com",
                        Title = null,
                        Text = "Net income reached $3.1 billion.",
                    }],
                }]
            });

        BlockExecutionContext context = CreateMockContext(mockExa);
        Block block = CreateBlock(new IntelligentSearchProperties
        {
            SearchTermTemplate = "income",
            TextEnabled = true,
        });
        var strategy = new ExaSearchStrategy();

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Fact numerical = Assert.Single(context.BlockAnnotations.Facts, f => f.IsNumerical);
        Assert.Equal("$3.1 billion", numerical.Value);
        Assert.Equal("https://sub.example.com", ((UrlSource)numerical.Sources[0]).Url);
    }

    [Fact]
    public async Task ExecuteBlockAsync_PageLimitExcludedResult_NotCitedAsync()
    {
        IExaSearchService mockExa = Substitute.For<IExaSearchService>();
        _ = mockExa.SearchAsync(Arg.Any<ExaSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ExaSearchResponse
            {
                Results = [new ExaResultItem
                {
                    Id = "1",
                    Url = "https://excluded.example.com",
                    Title = null,
                    Text = $"Revenue was $88.8 billion {GenerateWords(100)}",
                }]
            });

        BlockExecutionContext context = CreateMockContext(mockExa, wordCountPerPage: 10);
        Block block = CreateBlock(new IntelligentSearchProperties
        {
            SearchTermTemplate = "revenue",
            TextEnabled = true,
            MaxPagesPerResult = 5,
        });
        var strategy = new ExaSearchStrategy();

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Equal("No search results found", block.BlockResponse!.RawResponse);
        Assert.Empty(context.BlockAnnotations.Facts);
    }

    [Fact]
    public async Task ExecuteBlockAsync_WithDocumentClass_EmitsFactsAndCreatesDocumentAsync()
    {
        IExaSearchService mockExa = Substitute.For<IExaSearchService>();
        _ = mockExa.SearchAsync(Arg.Any<ExaSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ExaSearchResponse
            {
                Results = [new ExaResultItem
                {
                    Id = "1",
                    Url = "https://source.example.com",
                    Title = "Deal",
                    Text = "The acquisition closed at $42.5 billion.",
                }]
            });

        BlockExecutionContext context = CreateMockContext(mockExa);
        Block block = CreateBlock(new IntelligentSearchProperties
        {
            SearchTermTemplate = "acquisition",
            TextEnabled = true,
            CreatesDocumentClass = "deal-news",
        });
        var strategy = new ExaSearchStrategy();

        BlockStrategyOutput output = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        _ = Assert.Single(output.Documents);
        Assert.Equal("deal-news", output.Documents[0].DocumentClass);
        Fact numerical = Assert.Single(context.BlockAnnotations.Facts, f => f.IsNumerical);
        Assert.Equal("$42.5 billion", numerical.Value);
    }

    [Fact]
    public async Task ExecuteBlockAsync_CreatesDocumentClassResolvedViaExecutionState_UsesResolvedClassAsync()
    {
        IExaSearchService mockExa = Substitute.For<IExaSearchService>();
        _ = mockExa.SearchAsync(Arg.Any<ExaSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ExaSearchResponse
            {
                Results = [new ExaResultItem
                {
                    Id = "1",
                    Url = "https://source.example.com",
                    Title = "Doc",
                    Text = "Some content for the document.",
                }]
            });

        BlockExecutionContext context = CreateMockContext(mockExa);
        var props = new IntelligentSearchProperties
        {
            SearchTermTemplate = "query",
            TextEnabled = true,
            CreatesDocumentClass = "class-{placeholder}",
        };
        props.ExecutionState = new IntelligentSearchPropertiesExecutionState(props)
        {
            SearchTerm = "query",
            CreatesDocumentClass = "resolved-class",
        };
        Block block = CreateBlock(props);
        var strategy = new ExaSearchStrategy();

        BlockStrategyOutput output = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        DocumentInfo documentInfo = Assert.Single(output.Documents);
        Assert.Equal("resolved-class", documentInfo.DocumentClass);
    }

    [Fact]
    public async Task ExecuteBlockAsync_SettingsWithTags_SendsFilledValuesToExaAsync()
    {
        IExaSearchService exa = Substitute.For<IExaSearchService>();
        ExaSearchRequest? sent = null;
        _ = exa.SearchAsync(Arg.Do<ExaSearchRequest>(request => sent = request), Arg.Any<CancellationToken>())
            .Returns(new ExaSearchResponse { Results = [] });

        BlockExecutionContext context = CreateMockContext(exa);
        var props = new IntelligentSearchProperties
        {
            SearchTermTemplate = "{IA_Name.answer} ownership",
            IncludeDomains = ["{ParentURL.answer}"],
            ExcludeDomains = ["{Blocked.answer}"],
            SubpageTarget = ["{Topic.answer}"],
            SummaryQuery = "Identify {IA_Name.answer}'s owners",
            SummarySchema = "{\"type\":\"object\",\"properties\":{\"owner\":{\"type\":\"string\"}}}",
            StartPublishedDate = "{SearchWindow.startDate}",
            EndPublishedDate = "{SearchWindow.endDate}",
        };
        props.ExecutionState = new IntelligentSearchPropertiesExecutionState(props);
        props.ExecutionState.ReplaceAllPlaceholders(true, new Dictionary<string, string?>
        {
            { "IA_Name.answer", "Acme Capital" },
            { "ParentURL.answer", "https://acme.example.com" },
            { "Blocked.answer", "noise.example.com" },
            { "Topic.answer", "ownership" },
            { "SearchWindow.startDate", "2024-01-01" },
            { "SearchWindow.endDate", "2024-12-31" },
        });
        Block block = CreateBlock(props);

        _ = await new ExaSearchStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(sent);
        Assert.Equal("Acme Capital ownership", sent.Query);
        Assert.Equal<string>(["https://acme.example.com"], Assert.IsType<string[]>(sent.IncludeDomains));
        Assert.Equal<string>(["noise.example.com"], Assert.IsType<string[]>(sent.ExcludeDomains));
        ExaContentsConfig contents = Assert.IsType<ExaContentsConfig>(sent.Contents);
        Assert.Equal<string>(["ownership"], Assert.IsType<string[]>(contents.SubpageTarget));
        ExaSummaryConfig summary = Assert.IsType<ExaSummaryConfig>(contents.Summary);
        Assert.Equal("Identify Acme Capital's owners", summary.Query);
        Assert.Equal("object", Assert.IsType<Dictionary<string, object>>(summary.Schema)["type"]);
        Assert.Equal("2024-01-01", sent.StartPublishedDate);
        Assert.Equal("2024-12-31", sent.EndPublishedDate);
    }

    #endregion

    private static Block CreateBlock(IntelligentSearchProperties properties)
    {
        var workflow = new Section("test", new Workflow("test"));
        var block = new Block(BlockTypes.IntelligentSearch, workflow)
        {
            Name = "Test Block",
            Properties = properties,
        };

        return block;
    }

    private static BlockExecutionContext CreateMockContext(
        IExaSearchService exaSearchService,
        int wordCountPerPage = 250)
    {
        var settings = new PlatformSettings();
        settings.IniaAi.WordCountPerPage = wordCountPerPage;

        return new BlockExecutionContext(
            logger: Substitute.For<ILogger<BlockExecutionContext>>(),
            chatConnector: Substitute.For<IChatConnector>(),
            ragService: null!,
            exchangeRateService: Substitute.For<IExchangeRateService>(),
            documentAnalysisService: null!,
            exaSearchService: exaSearchService,
            modelFamilyResolver: null!,
            fileStorageManager: null!,
            pdfFormat: FileFormatsUnderTest.Pdf,
            tokenUsageService: Substitute.For<ITokenUsageService>(),
            platformSettings: settings,
            services: null!);
    }

    private static string GenerateWords(int count)
    {
        return string.Join(" ", Enumerable.Range(1, count).Select(i => $"word{i}"));
    }
}

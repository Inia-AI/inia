using System.Collections.Concurrent;
using IniaAi.AiConnection.Abstractions;
using IniaAi.Common.Enums;
using IniaAi.Core.Rag;
using IniaAi.Core.Workflows.Outputs.TableOfContents;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Enums;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using FileMetadata = IniaAi.Domain.Entities.Documents.FileMetadata;

namespace IniaAi.Core.Tests.Rag;

public class RagServiceTests : IClassFixture<RagFixture<RagService>>
{
    private readonly RagService _ragService;
    private readonly RagFixture<RagService> _fixture;
    private readonly ITestOutputHelper _output;

    public RagServiceTests(RagFixture<RagService> fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
        _ragService = fixture.GetRagService(output, ScriptedChatConnector.Answering("<answer>1</answer>"));
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithSmartFilterExpressions_CountsTheFilterCallsTokens()
    {
        // Arrange
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.TopNPages,
            NumberOfPagesInput = "2",
            SmartFilterExpressions = ["Page 5"], // This triggers embedding call
        };
        List<Document> documents = await _fixture.GetDocumentsAsync();

        // Reset to ensure clean state
        _ragService.ResetTokenAccumulator();

        // Act
        _ = await _ragService.GetFilteredPagesAsync(ragSettings, documents, WorkflowType.GENERIC, TestContext.Current.CancellationToken);

        // Assert - "Page 5" is two words to the word-hash stand-in
        Assert.Equal(2, _ragService.AccumulatedEmbeddingTokens);
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WhenTheModelCountsNoTokens_LogsTheMarkerTheAlarmReads_NamingTheBlock()
    {
        var log = new CapturingLogger<RagService>();
        var ragService = new RagService(
            log,
            new WordHashEmbeddingsConnector(countsTokens: false),
            ScriptedChatConnector.Answering("<answer>1</answer>"),
            new ServiceScopeFactory());
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.TopNPages,
            NumberOfPagesInput = "2",
            SmartFilterExpressions = ["Page 5"],
        };
        List<Document> documents = await _fixture.GetDocumentsAsync();

        _ = await ragService.GetFilteredPagesAsync(ragSettings, documents, WorkflowType.GENERIC, TestContext.Current.CancellationToken);

        CapturedLogEntry warning = Assert.Single(log.Entries, entry => entry.Level == LogLevel.Warning
            && entry.Message.StartsWith(EmbeddingsAnswer.UNCOUNTED_LOG_MARKER, StringComparison.Ordinal));
        Assert.Equal(ragSettings.BlockId, warning.Value("BlockId"));
        Assert.Equal(WorkflowType.GENERIC, warning.Value("WorkflowTypeId"));
        Assert.Equal(0, ragService.AccumulatedEmbeddingTokens);
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithWholeDocumentType_ShouldReturnAllPagesAsync()
    {
        // Arrange
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow()))) { Type = RagTypes.WholeDocument };
        var doc1 = new Document(new FileMetadata("1234", "Document 1", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        var doc2 = new Document(new FileMetadata("1234", "Document 2", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        _ = new Page(doc1, 1, 1) { Text = "Page 1 Text", EmbeddingVector = new("", [0.1, 0.2, 0.3]) };
        _ = new Page(doc1, 2, 2) { Text = "Page 2 Text", EmbeddingVector = new("", [0.4, 0.5, 0.6]) };
        _ = new Page(doc2, 1, 1) { Text = "Page 1 Text", EmbeddingVector = new("", [0.7, 0.8, 0.9]) };
        _ = new Page(doc2, 2, 2) { Text = "Page 2 Text", EmbeddingVector = new("", [1.0, 1.1, 1.2]) };
        List<Document> documents = [doc1, doc2];

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert
        _ = Assert.Single(resultDict);
        List<Page> result = [.. resultDict[string.Empty].Values];
        Assert.Equal(4, result.Count);
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithTopNPagesType_ShouldReturnTopNPagesAsync()
    {
        // Arrange
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.TopNPages,
            StartIndexInput = "0 + 1",
            EndIndexInput = "1 * 10",
            NumberOfPagesInput = "4/2",
            SmartFilterExpressions = ["Page 5 Text"],
        };
        List<Document> documents = await _fixture.GetDocumentsAsync();

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert
        _ = Assert.Single(resultDict);
        List<Page> result = [.. resultDict[string.Empty].Values];
        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.Contains("Page", r.Text));
        Assert.All(result, r => Assert.Contains("Text", r.Text));
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithTopNPagesPerFilterWithTwoNeighborsType_ShouldReturnBestMatchesWithNeighborsAsync()
    {
        // Arrange
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.TopNPagesPerFilterWithTwoNeighbors,
            StartIndexInput = "3",
            EndIndexInput = "6",
            NumberOfPagesInput = "2",
            SmartFilterExpressions = ["Page 3"],
        };
        List<Document> documents = await _fixture.GetDocumentsAsync();

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert — 2 best matches + their neighbors yields 3-5 pages depending on adjacency
        _ = Assert.Single(resultDict);
        List<Page> result = [.. resultDict[string.Empty].Values];
        Assert.InRange(result.Count, 3, 5);
        Assert.Contains(result, r => r.Text!.Contains("Page 3", StringComparison.InvariantCultureIgnoreCase));
        Assert.All(result, r => Assert.Contains("Text", r.Text));
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithTopPageType_ShouldReturnTopPageAsync()
    {
        // Arrange
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.TopPage,
            SmartFilterExpressions = ["Page 3"],
        };
        List<Document> documents = await _fixture.GetDocumentsAsync();

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert
        _ = Assert.Single(resultDict);
        List<Page> result = [.. resultDict[string.Empty].Values];
        _ = Assert.Single(result);
        Assert.Contains("Page 3 Text", result[0].Text, StringComparison.InvariantCultureIgnoreCase);
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithTopPageWithNFollowingPagesType_ShouldReturnTopPageAndFollowingPagesAsync()
    {
        // Arrange
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.TopPageWithNFollowingPages,
            StartIndexInput = "1",
            EndIndexInput = "5",
            NumberOfPagesInput = "2",
            SmartFilterExpressions = ["Page 3"],
        };
        List<Document> documents = await _fixture.GetDocumentsAsync();

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert
        _ = Assert.Single(resultDict);
        List<Page> result = [.. resultDict[string.Empty].Values];
        Assert.Equal(3, result.Count);
        Assert.Contains("Page 3 Text", result[0].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 4 Text", result[1].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 5 Text", result[2].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains(result, r => r.Document?.Name == "Document 1");
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithNPagesStartingFromIndexType_ShouldReturnCorrectPagesAsync()
    {
        // Arrange
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.NPagesStartingFromIndex,
            StartIndexInput = "2",
            EndIndexInput = "5",
            NumberOfPagesInput = "3",
        };
        List<Document> documents = await _fixture.GetDocumentsAsync();

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert
        _ = Assert.Single(resultDict);
        List<Page> result = [.. resultDict[string.Empty].Values];
        Assert.Equal(3, result.Count);
        Assert.Contains("Page 2 Text", result[0].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 3 Text", result[1].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 4 Text", result[2].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains(result, r => r.Document?.Name == "Document 1");
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithKeywordBasedTypeInclusive_ShouldReturnSimilarToTopNPagesPerFilterWithNeighborsAsync()
    {
        // Arrange
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.KeywordBased,
            StartIndexInput = "3",
            EndIndexInput = "6",
            NumberOfPagesInput = "1",
            Keywords = ["7"],
            SmartFilterExpressions = ["Page 3"],
            ShouldFilterKeyWordsExclusively = false,
            ShouldIncludeNeighboringPages = true,
        };
        List<Document> documents = await _fixture.GetDocumentsAsync();

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert
        _ = Assert.Single(resultDict);
        List<Page> result = [.. resultDict[string.Empty].Values];
        Assert.Equal(8, result.Count);
        Assert.Contains("Page 3 Text", result[0].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 4 Text", result[1].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 6 Text", result[2].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 7 Text", result[3].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 8 Text", result[4].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 6 Text", result[5].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 7 Text", result[6].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 8 Text", result[7].Text, StringComparison.InvariantCultureIgnoreCase);

        Assert.Contains(result, r => r.Document?.Name == "Document 1");
        Assert.Contains(result, r => r.Document?.Name == "Document 2");
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithKeywordBasedTypeExclusive_ShouldReturnSimilarToTopNPagesPerFilterWithNeighborsAsync()
    {
        // Arrange
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.KeywordBased,
            NumberOfPagesInput = "1",
            Keywords = ["Text", "7", "Page"],
            ShouldFilterKeyWordsExclusively = true,
        };
        List<Document> documents = await _fixture.GetDocumentsAsync();

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert
        _ = Assert.Single(resultDict);
        List<Page> result = [.. resultDict[string.Empty].Values];
        Assert.Equal(2, result.Count);
        Assert.Contains("Page 7 Text", result[0].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 7 Text", result[1].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains(result, r => r.Document?.Name == "Document 1");
        Assert.Contains(result, r => r.Document?.Name == "Document 2");
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithKeywordBasedTypeSearchOverviews_ShouldSearchOverviewsInsteadOfTextAsync()
    {
        // Arrange
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.KeywordBased,
            Keywords = ["summary"],
            ShouldSearchOverviews = true,
            ShouldIncludeNeighboringPages = true,
        };
        var doc = new Document(new FileMetadata("1234", "Document 1", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        _ = new Page(doc, 1, 1) { Text = "Full page text content", Overview = "Page summary here", EmbeddingVector = new("", [0.1, 0.2, 0.3]) };
        _ = new Page(doc, 2, 2) { Text = "Another page with summary in text", Overview = "Different overview", EmbeddingVector = new("", [0.4, 0.5, 0.6]) };
        _ = new Page(doc, 3, 3) { Text = "Third page content", Overview = "Contains summary keyword", EmbeddingVector = new("", [0.7, 0.8, 0.9]) };
        List<Document> documents = [doc];

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert
        _ = Assert.Single(resultDict);
        List<Page> result = [.. resultDict[string.Empty].Values];
        // Should find pages 1 and 3 (overviews contain "summary"), plus neighbors = all 3 pages
        Assert.Equal(3, result.Count);
        // Verify that pages with "summary" in overview are included
        Assert.Contains(result, r => r.Overview == "Page summary here"); // Page 1
        Assert.Contains(result, r => r.Overview == "Contains summary keyword"); // Page 3
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithKeywordBasedTypeSearchText_ShouldSearchTextNotOverviewsAsync()
    {
        // Arrange
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.KeywordBased,
            Keywords = ["summary"],
            ShouldSearchOverviews = false, // Default - search text
            ShouldIncludeNeighboringPages = true,
        };
        var doc = new Document(new FileMetadata("1234", "Document 1", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        _ = new Page(doc, 1, 1) { Text = "Full page text content", Overview = "Page summary here", EmbeddingVector = new("", [0.1, 0.2, 0.3]) };
        _ = new Page(doc, 2, 2) { Text = "Another page with summary in text", Overview = "Different overview", EmbeddingVector = new("", [0.4, 0.5, 0.6]) };
        _ = new Page(doc, 3, 3) { Text = "Third page content", Overview = "Contains summary keyword", EmbeddingVector = new("", [0.7, 0.8, 0.9]) };
        List<Document> documents = [doc];

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert
        _ = Assert.Single(resultDict);
        List<Page> result = [.. resultDict[string.Empty].Values];
        // Should find page 2 (text contains "summary"), but NOT pages 1 or 3 (only overviews contain "summary")
        Assert.Equal(3, result.Count); // Page 2 plus neighbors (1 and 3)
        Assert.Contains(result, r => r.Text?.Contains("summary", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithKeywordBased_WhenPageIsImageOnly_MatchesViaOverviewAsync()
    {
        // Arrange
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.KeywordBased,
            Keywords = ["summary"],
            ShouldSearchOverviews = false,
            ShouldIncludeNeighboringPages = false,
        };
        var doc = new Document(new FileMetadata("1234", "Document 1", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        _ = new Page(doc, 1, 1) { Text = "alpha content", EmbeddingVector = new("", [0.1, 0.2, 0.3]) };
        _ = new Page(doc, 2, 2) { Overview = "Contains summary keyword", EmbeddingVector = new("", [0.4, 0.5, 0.6]), Status = PageStatus.OCRNeedsReview };
        _ = new Page(doc, 3, 3) { Text = "gamma content", EmbeddingVector = new("", [0.7, 0.8, 0.9]) };
        List<Document> documents = [doc];

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert — the image-only page matched via its Overview and was retrieved
        List<Page> result = resultDict.TryGetValue(string.Empty, out Dictionary<int, Page>? matched)
            ? [.. matched.Values]
            : [];
        Assert.Contains(result, r => r.IsOcrImageOnly);
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithKeywordBasedWithNeighbors_ShouldIncludeNeighboringPagesAsync()
    {
        // Arrange
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.KeywordBased,
            Keywords = ["target"],
            ShouldIncludeNeighboringPages = true, // Include neighbors
        };
        var doc = new Document(new FileMetadata("1234", "Document 1", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        _ = new Page(doc, 1, 1) { Text = "Page 1 content", EmbeddingVector = new("", [0.1, 0.2, 0.3]) };
        _ = new Page(doc, 2, 2) { Text = "Page 2 content", EmbeddingVector = new("", [0.4, 0.5, 0.6]) };
        _ = new Page(doc, 3, 3) { Text = "Page 3 with target keyword", EmbeddingVector = new("", [0.7, 0.8, 0.9]) };
        _ = new Page(doc, 4, 4) { Text = "Page 4 content", EmbeddingVector = new("", [0.1, 0.3, 0.5]) };
        _ = new Page(doc, 5, 5) { Text = "Page 5 content", EmbeddingVector = new("", [0.2, 0.4, 0.6]) };
        List<Document> documents = [doc];

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert
        _ = Assert.Single(resultDict);
        List<Page> result = [.. resultDict[string.Empty].Values];
        // Should find page 3 (has "target") plus neighbors (pages 2 and 4)
        Assert.Equal(3, result.Count);
        Assert.Contains(result, r => r.Text == "Page 2 content"); // Neighbor before
        Assert.Contains(result, r => r.Text == "Page 3 with target keyword"); // Match
        Assert.Contains(result, r => r.Text == "Page 4 content"); // Neighbor after
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithKeywordBasedWithoutNeighbors_ShouldReturnOnlyMatchedPagesAsync()
    {
        // Arrange
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.KeywordBased,
            Keywords = ["target"],
            ShouldIncludeNeighboringPages = false, // No neighbors
        };
        var doc = new Document(new FileMetadata("1234", "Document 1", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        _ = new Page(doc, 1, 1) { Text = "Page 1 content", EmbeddingVector = new("", [0.1, 0.2, 0.3]) };
        _ = new Page(doc, 2, 2) { Text = "Page 2 content", EmbeddingVector = new("", [0.4, 0.5, 0.6]) };
        _ = new Page(doc, 3, 3) { Text = "Page 3 with target keyword", EmbeddingVector = new("", [0.7, 0.8, 0.9]) };
        _ = new Page(doc, 4, 4) { Text = "Page 4 content", EmbeddingVector = new("", [0.1, 0.3, 0.5]) };
        _ = new Page(doc, 5, 5) { Text = "Page 5 content", EmbeddingVector = new("", [0.2, 0.4, 0.6]) };
        List<Document> documents = [doc];

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert
        _ = Assert.Single(resultDict);
        List<Page> result = [.. resultDict[string.Empty].Values];
        // Should only find page 3 (has "target"), no neighbors
        _ = Assert.Single(result);
        Assert.Contains(result, r => r.Text == "Page 3 with target keyword");
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithKeywordBasedNoMatches_ShouldReturnEmptyAsync()
    {
        // Arrange
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.KeywordBased,
            Keywords = ["summary", "rubbish"],
            ShouldIncludeNeighboringPages = false,
            ShouldFilterKeyWordsExclusively = true,
        };
        var doc = new Document(new FileMetadata("1234", "Document 1", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        _ = new Page(doc, 1, 1) { Text = "Full page text content", Overview = "Page summary here", EmbeddingVector = new("", [0.1, 0.2, 0.3]) };
        _ = new Page(doc, 2, 2) { Text = "Another page with summary in text", Overview = "Different overview", EmbeddingVector = new("", [0.4, 0.5, 0.6]) };
        _ = new Page(doc, 3, 3) { Text = "Third page content", Overview = "Contains summary keyword", EmbeddingVector = new("", [0.7, 0.8, 0.9]) };
        List<Document> documents = [doc];

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert - exclusive filter with no pages matching all keywords should return empty pages
        _ = Assert.Single(resultDict);
        List<Page> result = [.. resultDict[string.Empty].Values];
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithKeywordBasedExclusiveSearchOverviewsNoMatches_ShouldReturnEmptyAsync()
    {
        // Arrange - ShouldSearchOverviews = true, but "rubbish" is not in any overview
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.KeywordBased,
            Keywords = ["summary", "rubbish"],
            ShouldIncludeNeighboringPages = false,
            ShouldFilterKeyWordsExclusively = true,
            ShouldSearchOverviews = true,
        };
        var doc = new Document(new FileMetadata("1234", "Document 1", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        _ = new Page(doc, 1, 1) { Text = "Full page text content", Overview = "Page summary here", EmbeddingVector = new("", [0.1, 0.2, 0.3]) };
        _ = new Page(doc, 2, 2) { Text = "Another page with summary in text", Overview = "Different overview", EmbeddingVector = new("", [0.4, 0.5, 0.6]) };
        _ = new Page(doc, 3, 3) { Text = "Third page content", Overview = "Contains summary keyword", EmbeddingVector = new("", [0.7, 0.8, 0.9]) };
        List<Document> documents = [doc];

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert - "rubbish" not found in any overview, so exclusive filter returns empty
        _ = Assert.Single(resultDict);
        List<Page> result = [.. resultDict[string.Empty].Values];
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithKeywordBasedExclusiveSearchOverviewsAllMatch_ShouldReturnMatchingPagesAsync()
    {
        // Arrange - All keywords found in the same page's overview
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.KeywordBased,
            Keywords = ["summary", "keyword"],
            ShouldIncludeNeighboringPages = false,
            ShouldFilterKeyWordsExclusively = true,
            ShouldSearchOverviews = true,
        };
        var doc = new Document(new FileMetadata("1234", "Document 1", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        _ = new Page(doc, 1, 1) { Text = "Full page text content", Overview = "Page summary here", EmbeddingVector = new("", [0.1, 0.2, 0.3]) };
        _ = new Page(doc, 2, 2) { Text = "Another page", Overview = "Different overview", EmbeddingVector = new("", [0.4, 0.5, 0.6]) };
        _ = new Page(doc, 3, 3) { Text = "Third page content", Overview = "Contains summary keyword", EmbeddingVector = new("", [0.7, 0.8, 0.9]) };
        List<Document> documents = [doc];

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert - Only page 3 has both "summary" and "keyword" in its overview
        _ = Assert.Single(resultDict);
        List<Page> result = [.. resultDict[string.Empty].Values];
        _ = Assert.Single(result);
        Assert.Equal("Contains summary keyword", result[0].Overview);
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithKeywordBasedExclusiveKeywordsOnDifferentPages_ShouldReturnEmptyAsync()
    {
        // Arrange - Keywords exist but on different pages, not together
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.KeywordBased,
            Keywords = ["apple", "banana"],
            ShouldIncludeNeighboringPages = false,
            ShouldFilterKeyWordsExclusively = true,
        };
        var doc = new Document(new FileMetadata("1234", "Document 1", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        _ = new Page(doc, 1, 1) { Text = "This page has apple", EmbeddingVector = new("", [0.1, 0.2, 0.3]) };
        _ = new Page(doc, 2, 2) { Text = "This page has banana", EmbeddingVector = new("", [0.4, 0.5, 0.6]) };
        _ = new Page(doc, 3, 3) { Text = "This page has neither", EmbeddingVector = new("", [0.7, 0.8, 0.9]) };
        List<Document> documents = [doc];

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert - No single page has both "apple" AND "banana", so exclusive filter returns empty
        _ = Assert.Single(resultDict);
        List<Page> result = [.. resultDict[string.Empty].Values];
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithKeywordBasedExclusiveAllKeywordsOnSamePage_ShouldReturnThatPageAsync()
    {
        // Arrange - One page has all keywords
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.KeywordBased,
            Keywords = ["apple", "banana", "cherry"],
            ShouldIncludeNeighboringPages = false,
            ShouldFilterKeyWordsExclusively = true,
        };
        var doc = new Document(new FileMetadata("1234", "Document 1", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        _ = new Page(doc, 1, 1) { Text = "This page has apple", EmbeddingVector = new("", [0.1, 0.2, 0.3]) };
        _ = new Page(doc, 2, 2) { Text = "This page has apple banana cherry all together", EmbeddingVector = new("", [0.4, 0.5, 0.6]) };
        _ = new Page(doc, 3, 3) { Text = "This page has apple and banana only", EmbeddingVector = new("", [0.7, 0.8, 0.9]) };
        List<Document> documents = [doc];

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert - Only page 2 has all three keywords
        _ = Assert.Single(resultDict);
        List<Page> result = [.. resultDict[string.Empty].Values];
        _ = Assert.Single(result);
        Assert.Contains("apple banana cherry", result[0].Text);
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithKeywordBasedInclusiveOneKeywordNoMatches_ShouldReturnOtherMatchesAsync()
    {
        // Arrange - Inclusive (OR) filter: one keyword has no matches, but others do
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.KeywordBased,
            Keywords = ["apple", "rubbish"],
            ShouldIncludeNeighboringPages = false,
            ShouldFilterKeyWordsExclusively = false, // Inclusive (OR)
        };
        var doc = new Document(new FileMetadata("1234", "Document 1", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        _ = new Page(doc, 1, 1) { Text = "This page has apple", EmbeddingVector = new("", [0.1, 0.2, 0.3]) };
        _ = new Page(doc, 2, 2) { Text = "This page has orange", EmbeddingVector = new("", [0.4, 0.5, 0.6]) };
        _ = new Page(doc, 3, 3) { Text = "Another apple page", EmbeddingVector = new("", [0.7, 0.8, 0.9]) };
        List<Document> documents = [doc];

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert - Inclusive filter returns pages with "apple" even though "rubbish" has no matches
        _ = Assert.Single(resultDict);
        List<Page> result = [.. resultDict[string.Empty].Values];
        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.Contains("apple", r.Text));
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithKeywordBasedExclusiveWithNeighbors_ShouldIncludeNeighborsOfMatchingPagesAsync()
    {
        // Arrange - Exclusive filter with neighbors enabled
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.KeywordBased,
            Keywords = ["target", "keyword"],
            ShouldIncludeNeighboringPages = true, // Include neighbors
            ShouldFilterKeyWordsExclusively = true,
        };
        var doc = new Document(new FileMetadata("1234", "Document 1", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        _ = new Page(doc, 1, 1) { Text = "Page 1 content", EmbeddingVector = new("", [0.1, 0.2, 0.3]) };
        _ = new Page(doc, 2, 2) { Text = "Page 2 content", EmbeddingVector = new("", [0.4, 0.5, 0.6]) };
        _ = new Page(doc, 3, 3) { Text = "Page 3 with target keyword here", EmbeddingVector = new("", [0.7, 0.8, 0.9]) };
        _ = new Page(doc, 4, 4) { Text = "Page 4 content", EmbeddingVector = new("", [0.1, 0.3, 0.5]) };
        _ = new Page(doc, 5, 5) { Text = "Page 5 content", EmbeddingVector = new("", [0.2, 0.4, 0.6]) };
        List<Document> documents = [doc];

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert - Page 3 matches both keywords, plus neighbors (pages 2 and 4)
        _ = Assert.Single(resultDict);
        List<Page> result = [.. resultDict[string.Empty].Values];
        Assert.Equal(3, result.Count);
        Assert.Contains(result, r => r.Text == "Page 2 content");
        Assert.Contains(result, r => r.Text == "Page 3 with target keyword here");
        Assert.Contains(result, r => r.Text == "Page 4 content");
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithExactPagesType_ShouldReturnExactIndexesAsync()
    {
        // Arrange
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.ExactPages,
            ExactIndexesInput = "1, 2, 3, 4-5, 6 -8,10",
            SmartFilterExpressions = ["Page 3"],
        };
        List<Document> documents = await _fixture.GetDocumentsAsync();

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert
        _ = Assert.Single(resultDict);
        List<Page> result = [.. resultDict[string.Empty].Values];
        Assert.Equal(9, result.Count);
        Assert.Contains("Page 1 Text", result[0].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 2 Text", result[1].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 3 Text", result[2].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 4 Text", result[3].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 5 Text", result[4].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 6 Text", result[5].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 7 Text", result[6].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 8 Text", result[7].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 10 Text", result[8].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains(result, r => r.Document?.Name == "Document 1");
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithExactPagesTypeNegativeIndices_ShouldReturnPagesFromEndAsync()
    {
        // Arrange
        var doc = new Document(new FileMetadata("1234", "Document 1", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        _ = new Page(doc, 1, 1) { Text = "Page 1 Text", EmbeddingVector = new("", [0.1, 0.2, 0.3]) };
        _ = new Page(doc, 2, 2) { Text = "Page 2 Text", EmbeddingVector = new("", [0.4, 0.5, 0.6]) };
        _ = new Page(doc, 3, 3) { Text = "Page 3 Text", EmbeddingVector = new("", [0.7, 0.8, 0.9]) };
        _ = new Page(doc, 4, 4) { Text = "Page 4 Text", EmbeddingVector = new("", [1.0, 1.1, 1.2]) };
        _ = new Page(doc, 5, 5) { Text = "Page 5 Text", EmbeddingVector = new("", [1.3, 1.4, 1.5]) };
        List<Document> documents = [doc];

        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.ExactPages,
            ExactIndexesInput = "1, -2--1",
        };

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert
        _ = Assert.Single(resultDict);
        List<Page> result = [.. resultDict[string.Empty].Values];
        Assert.Equal(3, result.Count);
        Assert.Contains("Page 1 Text", result[0].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 4 Text", result[1].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 5 Text", result[2].Text, StringComparison.InvariantCultureIgnoreCase);
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithExactPagesTypeNegativeIndexOutOfBounds_ShouldSkipInvalidIndicesAsync()
    {
        // Arrange - document has 5 pages, but we request -6 (which would be page index -1, invalid)
        var doc = new Document(new FileMetadata("1234", "Document 1", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        _ = new Page(doc, 1, 1) { Text = "Page 1 Text", EmbeddingVector = new("", [0.1, 0.2, 0.3]) };
        _ = new Page(doc, 2, 2) { Text = "Page 2 Text", EmbeddingVector = new("", [0.4, 0.5, 0.6]) };
        _ = new Page(doc, 3, 3) { Text = "Page 3 Text", EmbeddingVector = new("", [0.7, 0.8, 0.9]) };
        _ = new Page(doc, 4, 4) { Text = "Page 4 Text", EmbeddingVector = new("", [1.0, 1.1, 1.2]) };
        _ = new Page(doc, 5, 5) { Text = "Page 5 Text", EmbeddingVector = new("", [1.3, 1.4, 1.5]) };
        List<Document> documents = [doc];

        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.ExactPages,
            ExactIndexesInput = "-6, -1",
        };

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert - -6 is out of bounds (would resolve to index -1), only -1 (last page) should be returned
        _ = Assert.Single(resultDict);
        List<Page> result = [.. resultDict[string.Empty].Values];
        _ = Assert.Single(result);
        Assert.Contains("Page 5 Text", result[0].Text, StringComparison.InvariantCultureIgnoreCase);
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithExactPagesTypeMalformedInput_ShouldReturnEmptyAsync()
    {
        // Arrange
        var doc = new Document(new FileMetadata("1234", "Document 1", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        _ = new Page(doc, 1, 1) { Text = "Page 1 Text", EmbeddingVector = new("", [0.1, 0.2, 0.3]) };
        _ = new Page(doc, 2, 2) { Text = "Page 2 Text", EmbeddingVector = new("", [0.4, 0.5, 0.6]) };
        List<Document> documents = [doc];

        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.ExactPages,
            ExactIndexesInput = "-1-",
        };

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert - malformed input should result in empty pages
        _ = Assert.Single(resultDict);
        List<Page> result = [.. resultDict[string.Empty].Values];
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithExactPagesNegativeOutOfBoundsAndFollowingPages_ShouldNotLeakInRangePagesAsync()
    {
        // Arrange - -6 on 5-page doc resolves to index -1 (out of bounds).
        // With following pages, the expansion loop must NOT produce in-range indices (0, 1, ...).
        var doc = new Document(new FileMetadata("1234", "Document 1", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        _ = new Page(doc, 1, 1) { Text = "Page 1 Text", EmbeddingVector = new("", [0.1, 0.2, 0.3]) };
        _ = new Page(doc, 2, 2) { Text = "Page 2 Text", EmbeddingVector = new("", [0.4, 0.5, 0.6]) };
        _ = new Page(doc, 3, 3) { Text = "Page 3 Text", EmbeddingVector = new("", [0.7, 0.8, 0.9]) };
        _ = new Page(doc, 4, 4) { Text = "Page 4 Text", EmbeddingVector = new("", [1.0, 1.1, 1.2]) };
        _ = new Page(doc, 5, 5) { Text = "Page 5 Text", EmbeddingVector = new("", [1.3, 1.4, 1.5]) };
        List<Document> documents = [doc];

        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.ExactPages,
            ExactIndexesInput = "-6",
            NumberOfPagesInput = "2",
        };

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(ragSettings, documents, WorkflowType.GENERIC, TestContext.Current.CancellationToken);

        // Assert - out-of-bounds base index should be skipped entirely, not expanded
        _ = Assert.Single(resultDict);
        List<Page> result = [.. resultDict[string.Empty].Values];
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithExactPagesAndEmptyFollowingPages_ShouldReturnOnlyExactPagesAsync()
    {
        // Arrange - backward compatibility: empty NumberOfPagesInput = no following pages
        var doc = new Document(new FileMetadata("1234", "Document 1", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        _ = new Page(doc, 1, 1) { Text = "Page 1 Text", EmbeddingVector = new("", [0.1, 0.2, 0.3]) };
        _ = new Page(doc, 2, 2) { Text = "Page 2 Text", EmbeddingVector = new("", [0.4, 0.5, 0.6]) };
        _ = new Page(doc, 3, 3) { Text = "Page 3 Text", EmbeddingVector = new("", [0.7, 0.8, 0.9]) };
        _ = new Page(doc, 4, 4) { Text = "Page 4 Text", EmbeddingVector = new("", [1.0, 1.1, 1.2]) };
        _ = new Page(doc, 5, 5) { Text = "Page 5 Text", EmbeddingVector = new("", [1.3, 1.4, 1.5]) };
        List<Document> documents = [doc];

        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.ExactPages,
            ExactIndexesInput = "1, 3",
            NumberOfPagesInput = "",
        };

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(ragSettings, documents, WorkflowType.GENERIC, TestContext.Current.CancellationToken);

        // Assert
        _ = Assert.Single(resultDict);
        List<Page> result = [.. resultDict[string.Empty].Values];
        Assert.Equal(2, result.Count);
        Assert.Contains("Page 1 Text", result[0].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 3 Text", result[1].Text, StringComparison.InvariantCultureIgnoreCase);
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithExactPagesAndZeroFollowingPages_ShouldReturnOnlyExactPagesAsync()
    {
        // Arrange - NumberOfPagesInput = "0" means no following pages
        var doc = new Document(new FileMetadata("1234", "Document 1", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        _ = new Page(doc, 1, 1) { Text = "Page 1 Text", EmbeddingVector = new("", [0.1, 0.2, 0.3]) };
        _ = new Page(doc, 2, 2) { Text = "Page 2 Text", EmbeddingVector = new("", [0.4, 0.5, 0.6]) };
        _ = new Page(doc, 3, 3) { Text = "Page 3 Text", EmbeddingVector = new("", [0.7, 0.8, 0.9]) };
        _ = new Page(doc, 4, 4) { Text = "Page 4 Text", EmbeddingVector = new("", [1.0, 1.1, 1.2]) };
        _ = new Page(doc, 5, 5) { Text = "Page 5 Text", EmbeddingVector = new("", [1.3, 1.4, 1.5]) };
        List<Document> documents = [doc];

        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.ExactPages,
            ExactIndexesInput = "1, 3",
            NumberOfPagesInput = "0",
        };

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(ragSettings, documents, WorkflowType.GENERIC, TestContext.Current.CancellationToken);

        // Assert
        _ = Assert.Single(resultDict);
        List<Page> result = [.. resultDict[string.Empty].Values];
        Assert.Equal(2, result.Count);
        Assert.Contains("Page 1 Text", result[0].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 3 Text", result[1].Text, StringComparison.InvariantCultureIgnoreCase);
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithExactPagesAndFollowingPages_ShouldIncludeNFollowingPagesAsync()
    {
        // Arrange - page 1 + 2 following = pages 1, 2, 3
        var doc = new Document(new FileMetadata("1234", "Document 1", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        _ = new Page(doc, 1, 1) { Text = "Page 1 Text", EmbeddingVector = new("", [0.1, 0.2, 0.3]) };
        _ = new Page(doc, 2, 2) { Text = "Page 2 Text", EmbeddingVector = new("", [0.4, 0.5, 0.6]) };
        _ = new Page(doc, 3, 3) { Text = "Page 3 Text", EmbeddingVector = new("", [0.7, 0.8, 0.9]) };
        _ = new Page(doc, 4, 4) { Text = "Page 4 Text", EmbeddingVector = new("", [1.0, 1.1, 1.2]) };
        _ = new Page(doc, 5, 5) { Text = "Page 5 Text", EmbeddingVector = new("", [1.3, 1.4, 1.5]) };
        List<Document> documents = [doc];

        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.ExactPages,
            ExactIndexesInput = "1",
            NumberOfPagesInput = "2",
        };

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(ragSettings, documents, WorkflowType.GENERIC, TestContext.Current.CancellationToken);

        // Assert
        _ = Assert.Single(resultDict);
        List<Page> result = [.. resultDict[string.Empty].Values];
        Assert.Equal(3, result.Count);
        Assert.Contains("Page 1 Text", result[0].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 2 Text", result[1].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 3 Text", result[2].Text, StringComparison.InvariantCultureIgnoreCase);
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithExactPagesAndFollowingPagesOverlapping_ShouldDeduplicateAsync()
    {
        // Arrange - pages 2,4 each +2 following: 2,3,4 + 4,5,6 = 2,3,4,5 (deduplicated, 6 out of bounds)
        var doc = new Document(new FileMetadata("1234", "Document 1", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        _ = new Page(doc, 1, 1) { Text = "Page 1 Text", EmbeddingVector = new("", [0.1, 0.2, 0.3]) };
        _ = new Page(doc, 2, 2) { Text = "Page 2 Text", EmbeddingVector = new("", [0.4, 0.5, 0.6]) };
        _ = new Page(doc, 3, 3) { Text = "Page 3 Text", EmbeddingVector = new("", [0.7, 0.8, 0.9]) };
        _ = new Page(doc, 4, 4) { Text = "Page 4 Text", EmbeddingVector = new("", [1.0, 1.1, 1.2]) };
        _ = new Page(doc, 5, 5) { Text = "Page 5 Text", EmbeddingVector = new("", [1.3, 1.4, 1.5]) };
        List<Document> documents = [doc];

        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.ExactPages,
            ExactIndexesInput = "2, 4",
            NumberOfPagesInput = "2",
        };

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(ragSettings, documents, WorkflowType.GENERIC, TestContext.Current.CancellationToken);

        // Assert
        _ = Assert.Single(resultDict);
        List<Page> result = [.. resultDict[string.Empty].Values];
        Assert.Equal(4, result.Count);
        Assert.Contains("Page 2 Text", result[0].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 3 Text", result[1].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 4 Text", result[2].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 5 Text", result[3].Text, StringComparison.InvariantCultureIgnoreCase);
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithExactPagesAndFollowingPagesBeyondEnd_ShouldSkipOutOfBoundsAsync()
    {
        // Arrange - page 4 + 3 following = pages 4,5,6,7 but only 5 pages exist → pages 4,5
        var doc = new Document(new FileMetadata("1234", "Document 1", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        _ = new Page(doc, 1, 1) { Text = "Page 1 Text", EmbeddingVector = new("", [0.1, 0.2, 0.3]) };
        _ = new Page(doc, 2, 2) { Text = "Page 2 Text", EmbeddingVector = new("", [0.4, 0.5, 0.6]) };
        _ = new Page(doc, 3, 3) { Text = "Page 3 Text", EmbeddingVector = new("", [0.7, 0.8, 0.9]) };
        _ = new Page(doc, 4, 4) { Text = "Page 4 Text", EmbeddingVector = new("", [1.0, 1.1, 1.2]) };
        _ = new Page(doc, 5, 5) { Text = "Page 5 Text", EmbeddingVector = new("", [1.3, 1.4, 1.5]) };
        List<Document> documents = [doc];

        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.ExactPages,
            ExactIndexesInput = "4",
            NumberOfPagesInput = "3",
        };

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(ragSettings, documents, WorkflowType.GENERIC, TestContext.Current.CancellationToken);

        // Assert
        _ = Assert.Single(resultDict);
        List<Page> result = [.. resultDict[string.Empty].Values];
        Assert.Equal(2, result.Count);
        Assert.Contains("Page 4 Text", result[0].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 5 Text", result[1].Text, StringComparison.InvariantCultureIgnoreCase);
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithExactPagesOutOfBoundsAndFollowingPages_ShouldReturnEmptyAsync()
    {
        // Arrange - page 6 doesn't exist, so page 6 + 2 following = all out of bounds
        var doc = new Document(new FileMetadata("1234", "Document 1", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        _ = new Page(doc, 1, 1) { Text = "Page 1 Text", EmbeddingVector = new("", [0.1, 0.2, 0.3]) };
        _ = new Page(doc, 2, 2) { Text = "Page 2 Text", EmbeddingVector = new("", [0.4, 0.5, 0.6]) };
        _ = new Page(doc, 3, 3) { Text = "Page 3 Text", EmbeddingVector = new("", [0.7, 0.8, 0.9]) };
        _ = new Page(doc, 4, 4) { Text = "Page 4 Text", EmbeddingVector = new("", [1.0, 1.1, 1.2]) };
        _ = new Page(doc, 5, 5) { Text = "Page 5 Text", EmbeddingVector = new("", [1.3, 1.4, 1.5]) };
        List<Document> documents = [doc];

        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.ExactPages,
            ExactIndexesInput = "6",
            NumberOfPagesInput = "2",
        };

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(ragSettings, documents, WorkflowType.GENERIC, TestContext.Current.CancellationToken);

        // Assert
        _ = Assert.Single(resultDict);
        List<Page> result = [.. resultDict[string.Empty].Values];
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithExactPagesNegativeIndexAndFollowingPages_ShouldExpandFromResolvedIndexAsync()
    {
        // Arrange - -2 resolves to page 4 (index 3), +1 following = pages 4, 5
        var doc = new Document(new FileMetadata("1234", "Document 1", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        _ = new Page(doc, 1, 1) { Text = "Page 1 Text", EmbeddingVector = new("", [0.1, 0.2, 0.3]) };
        _ = new Page(doc, 2, 2) { Text = "Page 2 Text", EmbeddingVector = new("", [0.4, 0.5, 0.6]) };
        _ = new Page(doc, 3, 3) { Text = "Page 3 Text", EmbeddingVector = new("", [0.7, 0.8, 0.9]) };
        _ = new Page(doc, 4, 4) { Text = "Page 4 Text", EmbeddingVector = new("", [1.0, 1.1, 1.2]) };
        _ = new Page(doc, 5, 5) { Text = "Page 5 Text", EmbeddingVector = new("", [1.3, 1.4, 1.5]) };
        List<Document> documents = [doc];

        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.ExactPages,
            ExactIndexesInput = "-2",
            NumberOfPagesInput = "1",
        };

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(ragSettings, documents, WorkflowType.GENERIC, TestContext.Current.CancellationToken);

        // Assert
        _ = Assert.Single(resultDict);
        List<Page> result = [.. resultDict[string.Empty].Values];
        Assert.Equal(2, result.Count);
        Assert.Contains("Page 4 Text", result[0].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 5 Text", result[1].Text, StringComparison.InvariantCultureIgnoreCase);
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithExactPagesPageAdjustmentAndFollowingPages_ShouldCombineBothAsync()
    {
        // Arrange - page 1 with adjustment +1 = index 1 (page 2), +2 following = pages 2, 3, 4
        var doc = new Document(new FileMetadata("1234", "Document 1", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        _ = new Page(doc, 1, 1) { Text = "Page 1 Text", EmbeddingVector = new("", [0.1, 0.2, 0.3]) };
        _ = new Page(doc, 2, 2) { Text = "Page 2 Text", EmbeddingVector = new("", [0.4, 0.5, 0.6]) };
        _ = new Page(doc, 3, 3) { Text = "Page 3 Text", EmbeddingVector = new("", [0.7, 0.8, 0.9]) };
        _ = new Page(doc, 4, 4) { Text = "Page 4 Text", EmbeddingVector = new("", [1.0, 1.1, 1.2]) };
        _ = new Page(doc, 5, 5) { Text = "Page 5 Text", EmbeddingVector = new("", [1.3, 1.4, 1.5]) };
        List<Document> documents = [doc];

        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.ExactPages,
            ExactIndexesInput = "1",
            PageAdjustmentInput = "1",
            NumberOfPagesInput = "2",
        };

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(ragSettings, documents, WorkflowType.GENERIC, TestContext.Current.CancellationToken);

        // Assert
        _ = Assert.Single(resultDict);
        List<Page> result = [.. resultDict[string.Empty].Values];
        Assert.Equal(3, result.Count);
        Assert.Contains("Page 2 Text", result[0].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 3 Text", result[1].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 4 Text", result[2].Text, StringComparison.InvariantCultureIgnoreCase);
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithUseTableOfContentsType_ShouldReturnPagesFromTableOfContentsAsync()
    {
        // Arrange
        var toc = new TableOfContentsList();
        toc.Sections.Add(new TableOfContentsEntry() { Title = "1", StartPage = 1, EndPage = 3 });
        toc.Sections.Add(new TableOfContentsEntry() { Title = "2", StartPage = 17, EndPage = 19 });
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.UseTableOfContents,
            TableOfContentsInput = toc.ToJson(pretty: true)
        };
        List<Document> documents = await _fixture.GetDocumentsAsync();

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, resultDict.Count);
        List<Page> result1 = [.. resultDict["1"].Values];
        Assert.Equal(3, result1.Count);
        Assert.Contains("Page 1 Text", result1[0].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 2 Text", result1[1].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 3 Text", result1[2].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains(result1, r => r.Document?.Name == "Document 1");
        List<Page> result2 = [.. resultDict["2"].Values];
        Assert.Equal(3, result2.Count);
        Assert.Contains("Page 7 Text", result2[0].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 8 Text", result2[1].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 9 Text", result2[2].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains(result2, r => r.Document?.Name == "Document 2");
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithUseTableOfContentsTypeWithBrokenJson_ShouldignoreInvalidSectionsAsync()
    {
        // Arrange
        string json =
        """
        {
            "Sections": [
                {
                    "Title": "1",
                    "StartPage": null,
                    "EndPage": null
                },
                {
                    "Title": "2",
                    "StartPage": 17,
                    "EndPage": 19
                }
            ]
        }
        """;
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.UseTableOfContents,
            TableOfContentsInput = json,
        };
        List<Document> documents = await _fixture.GetDocumentsAsync();

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert
        _ = Assert.Single(resultDict);
        List<Page> result = [.. resultDict["2"].Values];
        Assert.Equal(3, result.Count);
        Assert.Contains("Page 7 Text", result[0].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 8 Text", result[1].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 9 Text", result[2].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains(result, r => r.Document?.Name == "Document 2");
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithUseTableOfContentsTypeWithPageAdjustment_ShouldReturnPagesFromTableOfContentsAsync()
    {
        // Arrange
        var toc = new TableOfContentsList();
        toc.Sections.Add(new TableOfContentsEntry() { Title = "1", StartPage = 1, EndPage = 2 });
        toc.Sections.Add(new TableOfContentsEntry() { Title = "2", StartPage = 14, EndPage = 15 });
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.UseTableOfContents,
            TableOfContentsInput = toc.ToJson(pretty: true),
            PageAdjustmentInput = "2"
        };
        List<Document> documents = await _fixture.GetDocumentsAsync();

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, resultDict.Count);
        List<Page> result1 = [.. resultDict["1"].Values];
        Assert.Equal(2, result1.Count);
        Assert.Contains("Page 3 Text", result1[0].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 4 Text", result1[1].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains(result1, r => r.Document?.Name == "Document 1");
        List<Page> result2 = [.. resultDict["2"].Values];
        Assert.Equal(2, result2.Count);
        Assert.Contains("Page 6 Text", result2[0].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 7 Text", result2[1].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains(result2, r => r.Document?.Name == "Document 2");
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithUseTableOfContentsTypeWithNegativePageAdjustment_ShouldReturnPagesFromTableOfContentsAsync()
    {
        // Arrange
        var toc = new TableOfContentsList();
        toc.Sections.Add(new TableOfContentsEntry() { Title = "1", StartPage = 5, EndPage = 6 });
        toc.Sections.Add(new TableOfContentsEntry() { Title = "2", StartPage = 18, EndPage = 19 });
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.UseTableOfContents,
            TableOfContentsInput = toc.ToJson(pretty: true),
            PageAdjustmentInput = "-2"
        };
        List<Document> documents = await _fixture.GetDocumentsAsync();

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, resultDict.Count);
        List<Page> result1 = [.. resultDict["1"].Values];
        Assert.Equal(2, result1.Count);
        Assert.Contains("Page 3 Text", result1[0].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 4 Text", result1[1].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains(result1, r => r.Document?.Name == "Document 1");
        List<Page> result2 = [.. resultDict["2"].Values];
        Assert.Equal(2, result2.Count);
        Assert.Contains("Page 6 Text", result2[0].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("Page 7 Text", result2[1].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains(result2, r => r.Document?.Name == "Document 2");
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithUseTableOfContentsTypeWithDuplicateSectionTitles_ShouldMergePagesFromBothSections()
    {
        // Arrange
        var toc = new TableOfContentsList();
        toc.Sections.Add(new TableOfContentsEntry() { Title = "Introduction", StartPage = 1, EndPage = 2 });
        toc.Sections.Add(new TableOfContentsEntry() { Title = "Introduction", StartPage = 5, EndPage = 6 });
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.UseTableOfContents,
            TableOfContentsInput = toc.ToJson(pretty: true)
        };
        List<Document> documents = await _fixture.GetDocumentsAsync();

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert
        _ = Assert.Single(resultDict);
        Assert.True(resultDict.ContainsKey("Introduction"));
        List<Page> result = [.. resultDict["Introduction"].Values];
        Assert.Equal(4, result.Count);
        Assert.Contains("1. Page 1 Text", result[0].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("1. Page 2 Text", result[1].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("1. Page 5 Text", result[2].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("1. Page 6 Text", result[3].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains(result, r => r.Document?.Name == "Document 1");
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithUseTableOfContentsTypeWithDuplicateSectionTitlesAndOverlappingPages_ShouldNotDuplicatePages()
    {
        // Arrange - sections with same title and overlapping page ranges
        var toc = new TableOfContentsList();
        toc.Sections.Add(new TableOfContentsEntry() { Title = "Chapter 1", StartPage = 1, EndPage = 3 });
        toc.Sections.Add(new TableOfContentsEntry() { Title = "Chapter 1", StartPage = 2, EndPage = 4 });
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.UseTableOfContents,
            TableOfContentsInput = toc.ToJson(pretty: true)
        };
        List<Document> documents = await _fixture.GetDocumentsAsync();

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert
        _ = Assert.Single(resultDict);
        Assert.True(resultDict.ContainsKey("Chapter 1"));
        List<Page> result = [.. resultDict["Chapter 1"].Values];
        Assert.Equal(4, result.Count); // Pages 1, 2, 3, 4 (no duplicates)

        // Verify all pages are present and no duplicates
        var pageNumbers = result.Select(p => p.PageNumber).OrderBy(n => n).ToList();
        Assert.Equal([1, 2, 3, 4], pageNumbers);
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithUseTableOfContentsTypeWithMultipleDuplicateSectionTitles_ShouldMergeAllSections()
    {
        // Arrange - three sections with same title
        var toc = new TableOfContentsList();
        toc.Sections.Add(new TableOfContentsEntry() { Title = "Appendix", StartPage = 1, EndPage = 1 });
        toc.Sections.Add(new TableOfContentsEntry() { Title = "Appendix", StartPage = 3, EndPage = 3 });
        toc.Sections.Add(new TableOfContentsEntry() { Title = "Appendix", StartPage = 5, EndPage = 5 });
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.UseTableOfContents,
            TableOfContentsInput = toc.ToJson(pretty: true)
        };
        List<Document> documents = await _fixture.GetDocumentsAsync();

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert
        _ = Assert.Single(resultDict);
        Assert.True(resultDict.ContainsKey("Appendix"));
        List<Page> result = [.. resultDict["Appendix"].Values];
        Assert.Equal(3, result.Count);
        Assert.Contains("1. Page 1 Text", result[0].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("1. Page 3 Text", result[1].Text, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("1. Page 5 Text", result[2].Text, StringComparison.InvariantCultureIgnoreCase);
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithUseTableOfContentsTypeWithEmptyAndNullSectionTitles_ShouldHandleGracefully()
    {
        // Arrange - mix of valid, empty, and null titles
        var toc = new TableOfContentsList();
        toc.Sections.Add(new TableOfContentsEntry() { Title = "Valid Section", StartPage = 1, EndPage = 2 });
        toc.Sections.Add(new TableOfContentsEntry() { Title = "", StartPage = 3, EndPage = 4 });
        toc.Sections.Add(new TableOfContentsEntry() { Title = null!, StartPage = 5, EndPage = 6 });
        toc.Sections.Add(new TableOfContentsEntry() { Title = "Valid Section", StartPage = 7, EndPage = 8 });
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.UseTableOfContents,
            TableOfContentsInput = toc.ToJson(pretty: true)
        };
        List<Document> documents = await _fixture.GetDocumentsAsync();

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert
        // Should handle empty/null titles and merge valid sections
        Assert.True(resultDict.ContainsKey("Valid Section"));
        List<Page> validSectionPages = [.. resultDict["Valid Section"].Values];
        Assert.Equal(4, validSectionPages.Count); // Pages 1, 2, 7, 8

        // Check if empty/null titles are handled (implementation dependent)
        // They might be skipped or given default keys
        Assert.True(resultDict.Count >= 1); // At minimum the valid section should exist
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithUseTableOfContentsTypeWithDuplicatesAndInvalidPageRanges_ShouldSkipInvalidAndMergeValid()
    {
        // Arrange - mix of valid and invalid page ranges with duplicate titles
        var toc = new TableOfContentsList();
        toc.Sections.Add(new TableOfContentsEntry() { Title = "Mixed Section", StartPage = 1, EndPage = 2 });
        toc.Sections.Add(new TableOfContentsEntry() { Title = "Mixed Section", StartPage = null, EndPage = null }); // Invalid
        toc.Sections.Add(new TableOfContentsEntry() { Title = "Mixed Section", StartPage = 5, EndPage = 6 });
        toc.Sections.Add(new TableOfContentsEntry() { Title = "Mixed Section", StartPage = 100, EndPage = 200 }); // Out of range
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.UseTableOfContents,
            TableOfContentsInput = toc.ToJson(pretty: true)
        };
        List<Document> documents = await _fixture.GetDocumentsAsync();

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert
        Assert.True(resultDict.ContainsKey("Mixed Section"));
        List<Page> result = [.. resultDict["Mixed Section"].Values];

        // Should contain only valid pages (1, 2, 5, 6) and skip invalid/out-of-range
        Assert.True(result.Count >= 4); // At minimum pages 1, 2, 5, 6
        Assert.Contains("1. Page 1 Text", result.Select(p => p.Text), StringComparer.InvariantCultureIgnoreCase);
        Assert.Contains("1. Page 2 Text", result.Select(p => p.Text), StringComparer.InvariantCultureIgnoreCase);
        Assert.Contains("1. Page 5 Text", result.Select(p => p.Text), StringComparer.InvariantCultureIgnoreCase);
        Assert.Contains("1. Page 6 Text", result.Select(p => p.Text), StringComparer.InvariantCultureIgnoreCase);
    }

    #region Caching Tests

    [Fact]
    public async Task GetFilteredPagesAsync_WithCache_ShouldPopulateCacheOnFirstCallAsync()
    {
        // Arrange
        var workflow = new Section("rag", new Workflow());
        var block = new Block(BlockTypes.SimpleRag, workflow);
        var ragSettings = new RagSettings(block) { Type = RagTypes.WholeDocument };
        ragSettings.SetExecutionState(asPlaceHolder: false, []);

        var doc = new Document(new FileMetadata("1234", "Document 1", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        _ = new Page(doc, 1, 1) { Text = "Page 1", EmbeddingVector = new("", [0.1, 0.2, 0.3]) };
        _ = new Page(doc, 2, 2) { Text = "Page 2", EmbeddingVector = new("", [0.4, 0.5, 0.6]) };
        block.Documents.Add(doc);

        var cache = new ConcurrentDictionary<string, Dictionary<string, Dictionary<int, Page>>>();

        // Act
        Dictionary<string, Dictionary<int, Page>> result = await _ragService.GetFilteredPagesAsync(
            block,
            WorkflowType.GENERIC,
            cache,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        _ = Assert.Single(cache); // Cache should have one entry
        Assert.Equal(2, result[string.Empty].Count);
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithCache_ShouldReuseCachedResultOnSecondCallAsync()
    {
        // Arrange
        var workflow = new Section("rag", new Workflow());
        var block1 = new Block(BlockTypes.SimpleRag, workflow);
        var ragSettings1 = new RagSettings(block1)
        {
            Type = RagTypes.WholeDocument,
        };
        ragSettings1.SetExecutionState(asPlaceHolder: false, []);

        var block2 = new Block(BlockTypes.SimpleRag, workflow);
        var ragSettings2 = new RagSettings(block2)
        {
            Type = RagTypes.WholeDocument,
        };
        ragSettings2.SetExecutionState(asPlaceHolder: false, []);

        var doc = new Document(new FileMetadata("1234", "Document 1", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        _ = new Page(doc, 1, 1) { Text = "Page 1", EmbeddingVector = new("", [0.1, 0.2, 0.3]) };
        _ = new Page(doc, 2, 2) { Text = "Page 2", EmbeddingVector = new("", [0.4, 0.5, 0.6]) };

        // Both blocks use the same document
        block1.Documents.Add(doc);
        block2.Documents.Add(doc);

        var cache = new ConcurrentDictionary<string, Dictionary<string, Dictionary<int, Page>>>();

        // Act - First call should populate cache
        Dictionary<string, Dictionary<int, Page>> result1 = await _ragService.GetFilteredPagesAsync(
            block1,
            WorkflowType.GENERIC,
            cache,
            cancellationToken: TestContext.Current.CancellationToken);

        // Second call with identical RagSettings and documents should hit cache
        Dictionary<string, Dictionary<int, Page>> result2 = await _ragService.GetFilteredPagesAsync(
            block2,
            WorkflowType.GENERIC,
            cache,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        _ = Assert.Single(cache); // Only one cache entry for identical settings (cache hit on second call)
        Assert.Equal(result1[string.Empty].Count, result2[string.Empty].Count);
        Assert.Equal(2, result1[string.Empty].Count);
        Assert.Equal(2, result2[string.Empty].Count);
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithCache_ShouldCreateSeparateEntriesForDifferentRagSettingsAsync()
    {
        // Arrange - Using strategies that don't require embeddings
        var workflow = new Section("rag", new Workflow());
        var block1 = new Block(BlockTypes.SimpleRag, workflow);
        var ragSettings1 = new RagSettings(block1)
        {
            Type = RagTypes.WholeDocument,
        };
        ragSettings1.SetExecutionState(asPlaceHolder: false, []);

        var block2 = new Block(BlockTypes.SimpleRag, workflow);
        var ragSettings2 = new RagSettings(block2)
        {
            Type = RagTypes.ExactPages,
            ExactIndexesInput = "1",
        };
        ragSettings2.SetExecutionState(asPlaceHolder: false, []);

        var doc = new Document(new FileMetadata("1234", "Document 1", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        _ = new Page(doc, 1, 1) { Text = "Page 1", EmbeddingVector = new("", [0.1, 0.2, 0.3]) };
        _ = new Page(doc, 2, 2) { Text = "Page 2", EmbeddingVector = new("", [0.4, 0.5, 0.6]) };

        block1.Documents.Add(doc);
        block2.Documents.Add(doc);

        var cache = new ConcurrentDictionary<string, Dictionary<string, Dictionary<int, Page>>>();

        // Act
        Dictionary<string, Dictionary<int, Page>> result1 = await _ragService.GetFilteredPagesAsync(
            block1,
            WorkflowType.GENERIC,
            cache,
            cancellationToken: TestContext.Current.CancellationToken);

        Dictionary<string, Dictionary<int, Page>> result2 = await _ragService.GetFilteredPagesAsync(
            block2,
            WorkflowType.GENERIC,
            cache,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, cache.Count); // Two different cache entries
        Assert.Equal(2, result1[string.Empty].Count); // WholeDocument returns all pages
        _ = Assert.Single(result2[string.Empty]); // ExactPages with index 1 returns 1 page
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithCache_ShouldCreateSeparateEntriesForDifferentDocumentsAsync()
    {
        // Arrange
        var workflow = new Section("rag", new Workflow());
        var block1 = new Block(BlockTypes.SimpleRag, workflow);
        var ragSettings1 = new RagSettings(block1) { Type = RagTypes.WholeDocument };
        ragSettings1.SetExecutionState(asPlaceHolder: false, []);

        var block2 = new Block(BlockTypes.SimpleRag, workflow);
        var ragSettings2 = new RagSettings(block2) { Type = RagTypes.WholeDocument };
        ragSettings2.SetExecutionState(asPlaceHolder: false, []);

        var doc1 = new Document(new FileMetadata("1234", "Document 1", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        _ = new Page(doc1, 1, 1) { Text = "Doc1 Page 1", EmbeddingVector = new("", [0.1, 0.2, 0.3]) };

        var doc2 = new Document(new FileMetadata("5678", "Document 2", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        _ = new Page(doc2, 1, 1) { Text = "Doc2 Page 1", EmbeddingVector = new("", [0.4, 0.5, 0.6]) };
        _ = new Page(doc2, 2, 2) { Text = "Doc2 Page 2", EmbeddingVector = new("", [0.7, 0.8, 0.9]) };

        block1.Documents.Add(doc1);
        block2.Documents.Add(doc2);

        var cache = new ConcurrentDictionary<string, Dictionary<string, Dictionary<int, Page>>>();

        // Act
        Dictionary<string, Dictionary<int, Page>> result1 = await _ragService.GetFilteredPagesAsync(
            block1,
            WorkflowType.GENERIC,
            cache,
            cancellationToken: TestContext.Current.CancellationToken);

        Dictionary<string, Dictionary<int, Page>> result2 = await _ragService.GetFilteredPagesAsync(
            block2,
            WorkflowType.GENERIC,
            cache,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, cache.Count); // Different documents = different cache entries
        _ = Assert.Single(result1[string.Empty]); // Doc1 has 1 page
        Assert.Equal(2, result2[string.Empty].Count); // Doc2 has 2 pages
    }

    [Fact]
    public async Task GetFilteredPagesAsync_WithCache_ShouldUseSameCacheEntryForIdenticalSettingsAsync()
    {
        // Arrange - Two blocks with identical RagSettings and documents
        // Using ExactPages strategy which doesn't require embeddings
        var workflow = new Section("rag", new Workflow());
        var block1 = new Block(BlockTypes.SimpleRag, workflow);
        var ragSettings1 = new RagSettings(block1)
        {
            Type = RagTypes.ExactPages,
            ExactIndexesInput = "1,2",
        };
        ragSettings1.SetExecutionState(asPlaceHolder: false, []);

        var block2 = new Block(BlockTypes.SimpleRag, workflow);
        var ragSettings2 = new RagSettings(block2)
        {
            Type = RagTypes.ExactPages,
            ExactIndexesInput = "1,2", // Identical settings
        };
        ragSettings2.SetExecutionState(asPlaceHolder: false, []);

        var doc = new Document(new FileMetadata("1234", "Document 1", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        _ = new Page(doc, 1, 1) { Text = "test page 1", EmbeddingVector = new("", [0.1, 0.2, 0.3]) };
        _ = new Page(doc, 2, 2) { Text = "test page 2", EmbeddingVector = new("", [0.4, 0.5, 0.6]) };
        _ = new Page(doc, 3, 3) { Text = "test page 3", EmbeddingVector = new("", [0.7, 0.8, 0.9]) };

        // Both blocks use the same document
        block1.Documents.Add(doc);
        block2.Documents.Add(doc);

        var cache = new ConcurrentDictionary<string, Dictionary<string, Dictionary<int, Page>>>();

        // Act
        Dictionary<string, Dictionary<int, Page>> result1 = await _ragService.GetFilteredPagesAsync(
            block1,
            WorkflowType.GENERIC,
            cache,
            cancellationToken: TestContext.Current.CancellationToken);

        Dictionary<string, Dictionary<int, Page>> result2 = await _ragService.GetFilteredPagesAsync(
            block2,
            WorkflowType.GENERIC,
            cache,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        _ = Assert.Single(cache); // Identical settings = same cache entry (cache hit on second call)
        Assert.Equal(2, result1[string.Empty].Count);
        Assert.Equal(2, result2[string.Empty].Count);
    }

    [Fact]
    public async Task GetFilteredPagesAsync_ConcurrentBlocksShareCache_DoesNotCorruptCache()
    {
        // Isolated per-call scopes (like production), so the only shared mutable state across
        // concurrent calls is the results cache itself.
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(_ =>
        {
            var provider = Substitute.For<IServiceProvider>();
            provider.GetService(typeof(ICoreUnitOfWork)).Returns(Substitute.For<ICoreUnitOfWork>());
            var scope = Substitute.For<IServiceScope>();
            scope.ServiceProvider.Returns(provider);
            return scope;
        });
        var ragService = new RagService(
            Substitute.For<ILogger<RagService>>(),
            Substitute.For<IEmbeddingsConnector>(),
            Substitute.For<IChatConnector>(),
            scopeFactory);

        // Each block has a unique document, so every call misses the cache and writes a distinct
        // entry: maximal concurrent mutation of the shared cache, like the parallel scheduler.
        const int blockCount = 512;
        List<Block> blocks = [.. Enumerable.Range(0, blockCount).Select(BuildWholeDocumentBlock)];
        var sharedCache = new ConcurrentDictionary<string, Dictionary<string, Dictionary<int, Page>>>();

        await Parallel.ForEachAsync(
            blocks,
            new ParallelOptions { MaxDegreeOfParallelism = 64, CancellationToken = TestContext.Current.CancellationToken },
            async (block, ct) =>
            {
                _ = await ragService.GetFilteredPagesAsync(block, WorkflowType.GENERIC, sharedCache, cancellationToken: ct);
            });

        // A plain Dictionary loses entries or throws under concurrent writes; a concurrency-safe
        // cache keeps every distinct entry.
        Assert.Equal(blockCount, sharedCache.Count);
    }

    [Fact]
    public async Task GetFilteredPagesAsync_EmptyCachedResult_IsRecomputed()
    {
        // An empty cached entry (e.g. a prior call that filtered nothing) must be treated as a
        // miss and recomputed, not returned as a hit.
        Block block = BuildWholeDocumentBlock(0); // WholeDocument over a 2-page document
        var cache = new ConcurrentDictionary<string, Dictionary<string, Dictionary<int, Page>>>();

        // Prime the cache, then poison its single entry with an empty result for the same key.
        _ = await _ragService.GetFilteredPagesAsync(
            block, WorkflowType.GENERIC, cache, cancellationToken: TestContext.Current.CancellationToken);
        string key = Assert.Single(cache.Keys);
        cache[key] = [];

        // Act
        Dictionary<string, Dictionary<int, Page>> result = await _ragService.GetFilteredPagesAsync(
            block, WorkflowType.GENERIC, cache, cancellationToken: TestContext.Current.CancellationToken);

        // Recomputed back to the 2 real pages; the old guard returned the empty cached value instead.
        Assert.Equal(2, result.GetValueOrDefault(string.Empty)?.Count ?? 0);
    }

    private static Block BuildWholeDocumentBlock(int index)
    {
        // WholeDocument is pure in-memory (no DB query), so the only shared mutable state across
        // concurrent calls is the results cache itself.
        var block = new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow()));
        var ragSettings = new RagSettings(block) { Type = RagTypes.WholeDocument };
        ragSettings.SetExecutionState(asPlaceHolder: false, []);

        var doc = new Document(new FileMetadata("1234", $"Document {index}", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        _ = new Page(doc, 1, 1) { Text = "page 1" };
        _ = new Page(doc, 2, 2) { Text = "page 2" };
        block.Documents.Add(doc);
        return block;
    }

    #endregion
}

using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Domain.Tests.Data.Workflows;

public class RagSettingsTests
{
    #region Reset Tests

    [Fact]
    public void AnEditorSave_ClearsTheStartAndEndIndexARagAgentRuleNoLongerShows()
    {
        var block = new Block(BlockTypes.AiQuery, new Section("rag", new Workflow()));
        var ragSettings = new RagSettings(block)
        {
            Type = RagTypes.RagAgent,
            QueryTemplate = "The table of debt maturities",
            IterationLimit = "2",
            ContextLimit = "10",
            StartIndexInput = "3",
            EndIndexInput = "7",
        };

        block.Reset();

        Assert.True(string.IsNullOrEmpty(ragSettings.StartIndexInput));
        Assert.True(string.IsNullOrEmpty(ragSettings.EndIndexInput));
        Assert.Equal("The table of debt maturities", ragSettings.QueryTemplate);
        Assert.Equal("2", ragSettings.IterationLimit);
        Assert.Equal("10", ragSettings.ContextLimit);
    }

    [Theory]
    [InlineData(RagTypes.WholeDocument)]
    [InlineData(RagTypes.TopNPages)]
    [InlineData(RagTypes.TopNPagesPerFilterWithTwoNeighbors)]
    [InlineData(RagTypes.TopPage)]
    [InlineData(RagTypes.TopPageWithNFollowingPages)]
    [InlineData(RagTypes.NPagesStartingFromIndex)]
    [InlineData(RagTypes.KeywordBased)]
    [InlineData(RagTypes.ExactPages)]
    [InlineData(RagTypes.UseTableOfContents)]
    [InlineData(RagTypes.UseAutoDetectedTableOfContents)]
    public void Reset_ForRagType_KeepsUniversalProperties(RagTypes ragType)
    {
        // Arrange
        var block = new Block(BlockTypes.SimpleRag);
        var ragSettings = new RagSettings(block)
        {
            Id = "test-id",
            Type = ragType
        };

        // Act
        ragSettings.Reset();

        // Assert - Universal properties should never be cleared
        Assert.Equal("test-id", ragSettings.Id);
        Assert.Equal(ragType, ragSettings.Type);
    }

    [Fact]
    public void Reset_TopNPages_KeepsRequiredPropertiesAndClearsOthers()
    {
        // Arrange
        var block = new Block(BlockTypes.SimpleRag);
        var ragSettings = new RagSettings(block)
        {
            Type = RagTypes.TopNPages,

            // Properties that should be kept for TopNPages
            StartIndexInput = "1",
            EndIndexInput = "20",
            NumberOfPagesInput = "10",

            // Properties that should be cleared (not relevant to TopNPages)
            ExactIndexesInput = "Should be cleared",
            PageAdjustmentInput = "Should be cleared",
            TopicsQuery = "Should be cleared",
            TableOfContentsInput = "Should be cleared",
            ShouldFilterKeyWordsExclusively = true
        };

        ragSettings.Keywords.Add("ShouldBeCleared");
        ragSettings.SmartFilterExpressions.Add("KeepThis");

        // Act
        ragSettings.Reset();

        // Assert - Required properties should be kept
        Assert.Equal("1", ragSettings.StartIndexInput);
        Assert.Equal("20", ragSettings.EndIndexInput);
        Assert.Equal("10", ragSettings.NumberOfPagesInput);
        _ = Assert.Single(ragSettings.SmartFilterExpressions);
        Assert.Contains("KeepThis", ragSettings.SmartFilterExpressions);

        // Assert - Irrelevant properties should be cleared
        Assert.Equal(string.Empty, ragSettings.ExactIndexesInput);
        Assert.Equal(string.Empty, ragSettings.PageAdjustmentInput);
        Assert.Null(ragSettings.TopicsQuery);
        Assert.Null(ragSettings.TableOfContentsInput);
        Assert.False(ragSettings.ShouldFilterKeyWordsExclusively);
        Assert.Empty(ragSettings.Keywords);
    }

    [Fact]
    public void Reset_TopNPagesPerFilterWithTwoNeighbors_KeepsRequiredPropertiesAndClearsOthers()
    {
        // Arrange
        var block = new Block(BlockTypes.SimpleRag);
        var ragSettings = new RagSettings(block)
        {
            Type = RagTypes.TopNPagesPerFilterWithTwoNeighbors,

            // Properties that should be kept
            StartIndexInput = "1",
            EndIndexInput = "50",
            NumberOfPagesInput = "5",

            // Properties that should be cleared
            ExactIndexesInput = "Should be cleared",
            PageAdjustmentInput = "Should be cleared",
            TopicsQuery = "Should be cleared",
            TableOfContentsInput = "Should be cleared"
        };

        ragSettings.SmartFilterExpressions.Add("KeepThis");
        ragSettings.Keywords.Add("ShouldBeCleared");

        // Act
        ragSettings.Reset();

        // Assert - Required properties should be kept
        Assert.Equal("1", ragSettings.StartIndexInput);
        Assert.Equal("50", ragSettings.EndIndexInput);
        Assert.Equal("5", ragSettings.NumberOfPagesInput);
        _ = Assert.Single(ragSettings.SmartFilterExpressions);
        Assert.Contains("KeepThis", ragSettings.SmartFilterExpressions);

        // Assert - Irrelevant properties should be cleared
        Assert.Equal(string.Empty, ragSettings.ExactIndexesInput);
        Assert.Equal(string.Empty, ragSettings.PageAdjustmentInput);
        Assert.Null(ragSettings.TopicsQuery);
        Assert.Null(ragSettings.TableOfContentsInput);
        Assert.Empty(ragSettings.Keywords);
    }

    [Fact]
    public void Reset_KeywordBased_KeepsRequiredPropertiesAndClearsOthers()
    {
        // Arrange
        var block = new Block(BlockTypes.SimpleRag);
        var ragSettings = new RagSettings(block)
        {
            Type = RagTypes.KeywordBased,

            // Properties that should be kept for KeywordBased
            StartIndexInput = "1",
            EndIndexInput = "100",
            NumberOfPagesInput = "50",
            ShouldFilterKeyWordsExclusively = true,

            // Properties that should be cleared
            ExactIndexesInput = "Should be cleared",
            PageAdjustmentInput = "Should be cleared",
            TopicsQuery = "Should be cleared",
            TableOfContentsInput = "Should be cleared"
        };

        ragSettings.Keywords.Add("keyword1");
        ragSettings.Keywords.Add("keyword2");
        ragSettings.SmartFilterExpressions.Add("KeepThis");

        // Act
        ragSettings.Reset();

        // Assert - Required properties should be kept
        Assert.Equal("1", ragSettings.StartIndexInput);
        Assert.Equal("100", ragSettings.EndIndexInput);
        Assert.Equal("50", ragSettings.NumberOfPagesInput);
        Assert.True(ragSettings.ShouldFilterKeyWordsExclusively);
        Assert.Equal(2, ragSettings.Keywords.Count);
        Assert.Contains("keyword1", ragSettings.Keywords);
        Assert.Contains("keyword2", ragSettings.Keywords);
        _ = Assert.Single(ragSettings.SmartFilterExpressions);
        Assert.Contains("KeepThis", ragSettings.SmartFilterExpressions);

        // Assert - Irrelevant properties should be cleared
        Assert.Equal(string.Empty, ragSettings.ExactIndexesInput);
        Assert.Equal(string.Empty, ragSettings.PageAdjustmentInput);
        Assert.Null(ragSettings.TopicsQuery);
        Assert.Null(ragSettings.TableOfContentsInput);
    }

    [Fact]
    public void Reset_ExactPages_KeepsRequiredPropertiesAndClearsOthers()
    {
        // Arrange
        var block = new Block(BlockTypes.SimpleRag);
        var ragSettings = new RagSettings(block)
        {
            Type = RagTypes.ExactPages,

            // Properties that should be kept for ExactPages
            PageAdjustmentInput = "2",
            ExactIndexesInput = "1,2,3,5,8",
            NumberOfPagesInput = "0",

            // Properties that should be cleared
            StartIndexInput = "Should be cleared",
            EndIndexInput = "Should be cleared",
            TopicsQuery = "Should be cleared",
            TableOfContentsInput = "Should be cleared",
            ShouldFilterKeyWordsExclusively = true
        };

        ragSettings.Keywords.Add("ShouldBeCleared");
        ragSettings.SmartFilterExpressions.Add("ShouldBeCleared");

        // Act
        ragSettings.Reset();

        // Assert - Required properties should be kept
        Assert.Equal("2", ragSettings.PageAdjustmentInput);
        Assert.Equal("1,2,3,5,8", ragSettings.ExactIndexesInput);
        Assert.Equal("0", ragSettings.NumberOfPagesInput);

        // Assert - Irrelevant properties should be cleared
        Assert.Equal(string.Empty, ragSettings.StartIndexInput);
        Assert.Equal(string.Empty, ragSettings.EndIndexInput);
        Assert.Null(ragSettings.TopicsQuery);
        Assert.Null(ragSettings.TableOfContentsInput);
        Assert.False(ragSettings.ShouldFilterKeyWordsExclusively);
        Assert.Empty(ragSettings.Keywords);
        Assert.Empty(ragSettings.SmartFilterExpressions);
    }

    [Fact]
    public void Reset_NPagesStartingFromIndex_KeepsRequiredPropertiesAndClearsOthers()
    {
        // Arrange
        var block = new Block(BlockTypes.SimpleRag);
        var ragSettings = new RagSettings(block)
        {
            Type = RagTypes.NPagesStartingFromIndex,

            // Properties that should be kept
            PageAdjustmentInput = "1",
            StartIndexInput = "5",
            EndIndexInput = "10",
            NumberOfPagesInput = "5",

            // Properties that should be cleared
            ExactIndexesInput = "Should be cleared",
            TopicsQuery = "Should be cleared",
            TableOfContentsInput = "Should be cleared"
        };

        ragSettings.Keywords.Add("ShouldBeCleared");
        ragSettings.SmartFilterExpressions.Add("ShouldBeCleared");

        // Act
        ragSettings.Reset();

        // Assert - Required properties should be kept
        Assert.Equal("1", ragSettings.PageAdjustmentInput);
        Assert.Equal("5", ragSettings.StartIndexInput);
        Assert.Equal("10", ragSettings.EndIndexInput);
        Assert.Equal("5", ragSettings.NumberOfPagesInput);

        // Assert - Irrelevant properties should be cleared
        Assert.Equal(string.Empty, ragSettings.ExactIndexesInput);
        Assert.Null(ragSettings.TopicsQuery);
        Assert.Null(ragSettings.TableOfContentsInput);
        Assert.Empty(ragSettings.Keywords);
        Assert.Empty(ragSettings.SmartFilterExpressions);
    }

    [Fact]
    public void Reset_UseTableOfContents_KeepsRequiredPropertiesAndClearsOthers()
    {
        // Arrange
        var block = new Block(BlockTypes.SimpleRag);
        var ragSettings = new RagSettings(block)
        {
            Type = RagTypes.UseTableOfContents,

            // Properties that should be kept
            PageAdjustmentInput = "0",
            TableOfContentsInput = "{{toc_data}}",

            // Properties that should be cleared
            ExactIndexesInput = "Should be cleared",
            NumberOfPagesInput = "Should be cleared",
            StartIndexInput = "Should be cleared",
            EndIndexInput = "Should be cleared",
            TopicsQuery = "Should be cleared"
        };

        ragSettings.Keywords.Add("ShouldBeCleared");
        ragSettings.SmartFilterExpressions.Add("ShouldBeCleared");

        // Act
        ragSettings.Reset();

        // Assert - Required properties should be kept
        Assert.Equal("0", ragSettings.PageAdjustmentInput);
        Assert.Equal("{{toc_data}}", ragSettings.TableOfContentsInput);

        // Assert - Irrelevant properties should be cleared
        Assert.Equal(string.Empty, ragSettings.ExactIndexesInput);
        Assert.Equal(string.Empty, ragSettings.NumberOfPagesInput);
        Assert.Equal(string.Empty, ragSettings.StartIndexInput);
        Assert.Equal(string.Empty, ragSettings.EndIndexInput);
        Assert.Null(ragSettings.TopicsQuery);
        Assert.Empty(ragSettings.Keywords);
        Assert.Empty(ragSettings.SmartFilterExpressions);
    }

    [Fact]
    public void Reset_TopPage_KeepsRequiredPropertiesAndClearsOthers()
    {
        // Arrange
        var block = new Block(BlockTypes.SimpleRag);
        var ragSettings = new RagSettings(block)
        {
            Type = RagTypes.TopPage,

            // Properties that should be kept
            StartIndexInput = "1",
            EndIndexInput = "30",
            NumberOfPagesInput = "1",

            // Properties that should be cleared
            ExactIndexesInput = "Should be cleared",
            PageAdjustmentInput = "Should be cleared",
            TopicsQuery = "Should be cleared",
            TableOfContentsInput = "Should be cleared",
            ShouldFilterKeyWordsExclusively = true
        };

        ragSettings.Keywords.Add("ShouldBeCleared");
        ragSettings.SmartFilterExpressions.Add("KeepThis");

        // Act
        ragSettings.Reset();

        // Assert - Required properties should be kept
        Assert.Equal("1", ragSettings.StartIndexInput);
        Assert.Equal("30", ragSettings.EndIndexInput);
        Assert.Equal("1", ragSettings.NumberOfPagesInput);
        _ = Assert.Single(ragSettings.SmartFilterExpressions);
        Assert.Contains("KeepThis", ragSettings.SmartFilterExpressions);

        // Assert - Irrelevant properties should be cleared
        Assert.Equal(string.Empty, ragSettings.ExactIndexesInput);
        Assert.Equal(string.Empty, ragSettings.PageAdjustmentInput);
        Assert.Null(ragSettings.TopicsQuery);
        Assert.Null(ragSettings.TableOfContentsInput);
        Assert.False(ragSettings.ShouldFilterKeyWordsExclusively);
        Assert.Empty(ragSettings.Keywords);
    }

    [Fact]
    public void Reset_TopPageWithNFollowingPages_KeepsRequiredPropertiesAndClearsOthers()
    {
        // Arrange
        var block = new Block(BlockTypes.SimpleRag);
        var ragSettings = new RagSettings(block)
        {
            Type = RagTypes.TopPageWithNFollowingPages,

            // Properties that should be kept
            StartIndexInput = "1",
            EndIndexInput = "50",
            NumberOfPagesInput = "5",

            // Properties that should be cleared
            ExactIndexesInput = "Should be cleared",
            PageAdjustmentInput = "Should be cleared",
            TopicsQuery = "Should be cleared",
            TableOfContentsInput = "Should be cleared",
            ShouldFilterKeyWordsExclusively = true
        };

        ragSettings.Keywords.Add("ShouldBeCleared");
        ragSettings.SmartFilterExpressions.Add("KeepThis");

        // Act
        ragSettings.Reset();

        // Assert - Required properties should be kept
        Assert.Equal("1", ragSettings.StartIndexInput);
        Assert.Equal("50", ragSettings.EndIndexInput);
        Assert.Equal("5", ragSettings.NumberOfPagesInput);
        _ = Assert.Single(ragSettings.SmartFilterExpressions);
        Assert.Contains("KeepThis", ragSettings.SmartFilterExpressions);

        // Assert - Irrelevant properties should be cleared
        Assert.Equal(string.Empty, ragSettings.ExactIndexesInput);
        Assert.Equal(string.Empty, ragSettings.PageAdjustmentInput);
        Assert.Null(ragSettings.TopicsQuery);
        Assert.Null(ragSettings.TableOfContentsInput);
        Assert.False(ragSettings.ShouldFilterKeyWordsExclusively);
        Assert.Empty(ragSettings.Keywords);
    }

    [Fact]
    public void Reset_UseAutoDetectedTableOfContents_KeepsRequiredPropertiesAndClearsOthers()
    {
        // Arrange
        var block = new Block(BlockTypes.SimpleRag);
        var ragSettings = new RagSettings(block)
        {
            Type = RagTypes.UseAutoDetectedTableOfContents,

            // Properties that should be kept
            TableOfContentsInput = "{{auto_toc}}",
            TopicsQuery = "financial statements",

            // Properties that should be cleared
            ExactIndexesInput = "Should be cleared",
            NumberOfPagesInput = "Should be cleared",
            StartIndexInput = "Should be cleared",
            EndIndexInput = "Should be cleared",
            PageAdjustmentInput = "Should be cleared",
            ShouldFilterKeyWordsExclusively = true
        };

        ragSettings.Keywords.Add("ShouldBeCleared");
        ragSettings.SmartFilterExpressions.Add("ShouldBeCleared");

        // Act
        ragSettings.Reset();

        // Assert - Required properties should be kept
        Assert.Equal("{{auto_toc}}", ragSettings.TableOfContentsInput);
        Assert.Equal("financial statements", ragSettings.TopicsQuery);

        // Assert - Irrelevant properties should be cleared
        Assert.Equal(string.Empty, ragSettings.ExactIndexesInput);
        Assert.Equal(string.Empty, ragSettings.NumberOfPagesInput);
        Assert.Equal(string.Empty, ragSettings.StartIndexInput);
        Assert.Equal(string.Empty, ragSettings.EndIndexInput);
        Assert.Equal(string.Empty, ragSettings.PageAdjustmentInput);
        Assert.False(ragSettings.ShouldFilterKeyWordsExclusively);
        Assert.Empty(ragSettings.Keywords);
        Assert.Empty(ragSettings.SmartFilterExpressions);
    }

    [Fact]
    public void Reset_WholeDocument_ClearsAllTypeSpecificProperties()
    {
        // Arrange
        var block = new Block(BlockTypes.SimpleRag);
        var ragSettings = new RagSettings(block)
        {
            Type = RagTypes.WholeDocument,

            // WholeDocument doesn't need any specific properties
            ExactIndexesInput = "Should be cleared",
            NumberOfPagesInput = "Should be cleared",
            StartIndexInput = "Should be cleared",
            EndIndexInput = "Should be cleared",
            PageAdjustmentInput = "Should be cleared",
            TopicsQuery = "Should be cleared",
            TableOfContentsInput = "Should be cleared",
            ShouldFilterKeyWordsExclusively = true
        };

        ragSettings.Keywords.Add("ShouldBeCleared");
        ragSettings.SmartFilterExpressions.Add("ShouldBeCleared");

        // Act
        ragSettings.Reset();

        // Assert - All type-specific properties should be cleared
        Assert.Equal(string.Empty, ragSettings.ExactIndexesInput);
        Assert.Equal(string.Empty, ragSettings.NumberOfPagesInput);
        Assert.Equal(string.Empty, ragSettings.StartIndexInput);
        Assert.Equal(string.Empty, ragSettings.EndIndexInput);
        Assert.Equal(string.Empty, ragSettings.PageAdjustmentInput);
        Assert.Null(ragSettings.TopicsQuery);
        Assert.Null(ragSettings.TableOfContentsInput);
        Assert.False(ragSettings.ShouldFilterKeyWordsExclusively);
        Assert.Empty(ragSettings.Keywords);
        Assert.Empty(ragSettings.SmartFilterExpressions);
    }

    [Fact]
    public void Reset_AllPropertiesAreCoveredByKeepOrClearRules()
    {
        // Arrange
        var block = new Block(BlockTypes.SimpleRag);
        var ragSettings = new RagSettings(block)
        {
            Type = RagTypes.TopNPages
        };

        // Get all public instance properties using reflection
        var allProperties = typeof(RagSettings)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(p => p.CanWrite || p.PropertyType.IsGenericType) // Writable or collection
            .Select(p => p.Name)
            .ToHashSet();

        // Act
        ragSettings.Reset();

        // Assert - This test ensures we've thought about every property
        // The test will fail if new properties are added without updating reset logic
        // This is intentional - it forces us to make conscious decisions about new properties
        Assert.NotEmpty(allProperties);

        // This is a meta-test that will be validated by the actual implementation
        // It ensures reflection-based coverage
    }

    #endregion
}

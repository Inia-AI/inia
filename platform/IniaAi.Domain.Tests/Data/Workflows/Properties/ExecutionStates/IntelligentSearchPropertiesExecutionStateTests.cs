using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using Newtonsoft.Json.Linq;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties.ExecutionStates;

public class IntelligentSearchPropertiesExecutionStateTests
{
    [Fact]
    public void Constructor_CopiesAllFieldsFromSource()
    {
        // Arrange
        var props = new IntelligentSearchProperties
        {
            SearchTermTemplate = "search {tag}",
            CreatesDocumentClass = "doc {tag}",
            SearchType = "type {tag}",
            SummaryQuery = "query {tag}",
            SummarySchema = "schema {tag}",
            Category = "cat {tag}",
            StartPublishedDate = "start {tag}",
            EndPublishedDate = "end {tag}",
            LiveCrawlStrategy = "crawl {tag}"
        };

        // Act
        var state = new IntelligentSearchPropertiesExecutionState(props);

        // Assert
        Assert.Equal("search {tag}", state.SearchTerm);
        Assert.Equal("doc {tag}", state.CreatesDocumentClass);
        Assert.Equal("type {tag}", state.SearchType);
        Assert.Equal("query {tag}", state.SummaryQuery);
        Assert.Equal("schema {tag}", state.SummarySchema);
        Assert.Equal("cat {tag}", state.Category);
        Assert.Equal("start {tag}", state.StartPublishedDate);
        Assert.Equal("end {tag}", state.EndPublishedDate);
        Assert.Equal("crawl {tag}", state.LiveCrawlStrategy);
    }

    [Fact]
    public void ReplaceAllPlaceholders_ReplacesInAllFields()
    {
        // Arrange
        var props = new IntelligentSearchProperties
        {
            SearchTermTemplate = "prefix {tag} suffix",
            CreatesDocumentClass = "prefix {tag} suffix",
            SearchType = "prefix {tag} suffix",
            SummaryQuery = "prefix {tag} suffix",
            SummarySchema = "prefix {tag} suffix",
            Category = "prefix {tag} suffix",
            StartPublishedDate = "prefix {tag} suffix",
            EndPublishedDate = "prefix {tag} suffix",
            LiveCrawlStrategy = "prefix {tag} suffix"
        };
        var state = new IntelligentSearchPropertiesExecutionState(props);

        // Act
        state.ReplaceAllPlaceholders(true, new Dictionary<string, string?> { { "tag", "resolved" } });

        // Assert
        Assert.Equal("prefix resolved suffix", state.SearchTerm);
        Assert.Equal("prefix resolved suffix", state.CreatesDocumentClass);
        Assert.Equal("prefix resolved suffix", state.SearchType);
        Assert.Equal("prefix resolved suffix", state.SummaryQuery);
        Assert.Equal("prefix resolved suffix", state.SummarySchema);
        Assert.Equal("prefix resolved suffix", state.Category);
        Assert.Equal("prefix resolved suffix", state.StartPublishedDate);
        Assert.Equal("prefix resolved suffix", state.EndPublishedDate);
        Assert.Equal("prefix resolved suffix", state.LiveCrawlStrategy);
    }

    [Fact]
    public void ReplaceAllPlaceholders_FillsDomainAndSubpageLists()
    {
        var props = new IntelligentSearchProperties
        {
            IncludeDomains = ["{ParentURL.answer}"],
            ExcludeDomains = ["{Blocked.answer}"],
            SubpageTarget = ["{Topic.answer}"],
        };
        var state = new IntelligentSearchPropertiesExecutionState(props);

        state.ReplaceAllPlaceholders(true, new Dictionary<string, string?>
        {
            { "ParentURL.answer", "https://manager.example.com" },
            { "Blocked.answer", "noise.example.com" },
            { "Topic.answer", "ownership" },
        });

        Assert.Equal(["https://manager.example.com"], state.IncludeDomains);
        Assert.Equal(["noise.example.com"], state.ExcludeDomains);
        Assert.Equal(["ownership"], state.SubpageTarget);
        Assert.Equal(["{ParentURL.answer}"], props.IncludeDomains);
    }

    [Fact]
    public void ReplaceAllPlaceholders_ValueWithQuotesInSummarySchema_KeepsTheSchemaValidJson()
    {
        var props = new IntelligentSearchProperties
        {
            SummarySchema = """{"type":"object","description":"Owners of {IA_Name.answer}"}""",
        };
        var state = new IntelligentSearchPropertiesExecutionState(props);

        state.ReplaceAllPlaceholders(true, new Dictionary<string, string?> { { "IA_Name.answer", "Acme \"Capital\" \\ Ltd" } });

        JObject schema = JObject.Parse(state.SummarySchema!);
        Assert.Equal("Owners of Acme \"Capital\" \\ Ltd", (string?)schema["description"]);
    }

    [Fact]
    public void ReplaceAllPlaceholders_NullFields_DoesNotThrow()
    {
        // Arrange
        var props = new IntelligentSearchProperties();
        var state = new IntelligentSearchPropertiesExecutionState(props);

        // Act & Assert — should not throw
        state.ReplaceAllPlaceholders(true, new Dictionary<string, string?> { { "tag", "resolved" } });
        Assert.Null(state.SearchTerm);
        Assert.Null(state.CreatesDocumentClass);
    }

    [Fact]
    public void SetExecutionState_OnProperties_CreatesAndResolvesState()
    {
        // Arrange
        var props = new IntelligentSearchProperties { SearchTermTemplate = "prefix {tag} suffix" };

        // Act
        props.SetExecutionState(true, new Dictionary<string, string?> { { "tag", "resolved" } });

        // Assert
        Assert.NotNull(props.ExecutionState);
        Assert.Equal("prefix resolved suffix", props.ExecutionState.SearchTerm);
    }

    [Fact]
    public void UpdateExecutionState_WhenNull_CreatesState()
    {
        // Arrange
        var props = new IntelligentSearchProperties { SearchTermTemplate = "prefix {tag} suffix" };
        Assert.Null(props.ExecutionState);

        // Act
        props.UpdateExecutionState(true, new Dictionary<string, string?> { { "tag", "resolved" } });

        // Assert
        Assert.NotNull(props.ExecutionState);
        Assert.Equal("prefix resolved suffix", props.ExecutionState.SearchTerm);
    }
}

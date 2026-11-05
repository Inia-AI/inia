using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Enums;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties;

public class BlockPropertiesCloneTests
{
    [Fact]
    public void AiQueryProperties_Clone_CopiesEveryField()
    {
        var source = new AiQueryProperties
        {
            UserPromptTemplate = "user {tag}",
            UserPromptTemplatePreview = "user preview",
            SystemPromptTemplate = "system {tag}",
            TextToCache = "cache-text",
            MaxTokens = 12345,
            ShouldUseAdvancedModel = true,
            ModelIdOverride = "claude-opus-4-6",
            ShouldChunkLargeInputContexts = true,
            ChunkSize = 7,
            ChunkingMode = ChunkingMode.OptimalCharacters,
            MaxChunkCharacters = 50_000,
            PreservePageOrder = true,
            ShouldUsePageImages = true,
            ShouldUseThinkingMode = true,
            ThinkingBudget = 2048,
            Effort = ModelEffort.Max,
            ShouldUseCachingForInputContext = true,
            ShouldGetCitationsFromModel = true,
            ShouldUseOverviews = true,
            ConditionalTagTemplate = "answer",
            ExecutionState = new AiQueryPropertiesExecutionState(new AiQueryProperties()),
        };

        var clone = (AiQueryProperties)source.Clone();

        CloneAssertions.AssertAllCloneableFieldsCopied(source, clone);
        Assert.Same(source.ExecutionState, clone.ExecutionState);
    }

    [Fact]
    public void MergeProperties_Clone_CopiesEveryField()
    {
        var source = new MergeProperties
        {
            TextTemplate = "merge {a} with {b}",
            ExecutionState = new MergePropertiesExecutionState(new MergeProperties()),
        };

        var clone = (MergeProperties)source.Clone();

        CloneAssertions.AssertAllCloneableFieldsCopied(source, clone);
        Assert.Same(source.ExecutionState, clone.ExecutionState);
    }

    [Fact]
    public void MergeJsonProperties_Clone_CopiesEveryField()
    {
        var source = new MergeJsonProperties
        {
            JsonInputs = { "{block_0}", "{\"a\":1}" },
            ExecutionState = new MergeJsonPropertiesExecutionState(new MergeJsonProperties()),
        };

        var clone = (MergeJsonProperties)source.Clone();

        CloneAssertions.AssertAllCloneableFieldsCopied(source, clone);
        Assert.NotSame(source.JsonInputs, clone.JsonInputs);
        Assert.Same(source.ExecutionState, clone.ExecutionState);
    }

    [Fact]
    public void SwitchCaseProperties_Clone_CopiesEveryField()
    {
        var source = new SwitchCaseProperties
        {
            Cases = new Dictionary<string, string>
            {
                { "key1", "value1" },
                { "key2", "value2" },
            },
            DefaultValue = "fallback",
            InputTemplate = "input {tag}",
            ExecutionState = new SwitchCasePropertiesExecutionState(new SwitchCaseProperties()),
        };

        var clone = (SwitchCaseProperties)source.Clone();

        CloneAssertions.AssertAllCloneableFieldsCopied(source, clone);
        Assert.NotSame(source.Cases, clone.Cases);
        Assert.Same(source.ExecutionState, clone.ExecutionState);
    }

    [Fact]
    public void BuildTopicsProperties_Clone_CopiesEveryField()
    {
        var source = new BuildTopicsProperties
        {
            MaxTopics = 25,
            TopicsModelTemplate = "topics {tag}",
            ExecutionState = new BuildTopicsPropertiesExecutionState(new BuildTopicsProperties()),
        };

        var clone = (BuildTopicsProperties)source.Clone();

        CloneAssertions.AssertAllCloneableFieldsCopied(source, clone);
        Assert.Same(source.ExecutionState, clone.ExecutionState);
    }

    [Fact]
    public void ExchangeRateProperties_Clone_CopiesEveryField()
    {
        var source = new ExchangeRateProperties
        {
            FromCurrency = "USD",
            ToCurrency = "EUR",
            ExchangeDate = "2026-04-15",
            UseCurrentDate = false,
            OperationMode = ExchangeRateOperationMode.Convert,
            InputValue = "123.45",
            ExecutionState = new ExchangeRatePropertiesExecutionState(new ExchangeRateProperties()),
        };

        var clone = (ExchangeRateProperties)source.Clone();

        CloneAssertions.AssertAllCloneableFieldsCopied(source, clone);
        Assert.Same(source.ExecutionState, clone.ExecutionState);
    }

    [Fact]
    public void IntelligentSearchProperties_Clone_CopiesEveryField()
    {
        var source = new IntelligentSearchProperties
        {
            SearchTermTemplate = "search {tag}",
            SearchType = "neural",
            NumResults = 20,
            TextEnabled = true,
            IncludeHtmlTags = false,
            MaxCharacters = 5000,
            HighlightsEnabled = true,
            SummaryEnabled = true,
            SummaryQuery = "summarize this",
            SummarySchema = "{\"type\":\"object\"}",
            ExcludeDomains = ["spam.com", "bad.org"],
            IncludeDomains = ["good.com"],
            Category = "news",
            StartPublishedDate = "2026-01-01",
            EndPublishedDate = "2026-04-15",
            LiveCrawlStrategy = "always",
            LiveCrawlTimeout = 30,
            Subpages = 5,
            SubpageTarget = ["pricing", "about"],
            MaxPagesPerResult = 50,
            CreatesDocumentClass = "ResearchDoc",
            ExecutionState = new IntelligentSearchPropertiesExecutionState(new IntelligentSearchProperties()),
        };

        var clone = (IntelligentSearchProperties)source.Clone();

        CloneAssertions.AssertAllCloneableFieldsCopied(source, clone);
        Assert.NotSame(source.ExcludeDomains, clone.ExcludeDomains);
        Assert.NotSame(source.IncludeDomains, clone.IncludeDomains);
        Assert.NotSame(source.SubpageTarget, clone.SubpageTarget);
        Assert.Same(source.ExecutionState, clone.ExecutionState);
    }

    [Fact]
    public void Json2XlsxProperties_Clone_CopiesEveryField()
    {
        var source = new Json2XlsxProperties
        {
            JsonInputTemplate = "json {tag}",
            ConversionType = "PortfolioHoldings",
            MappingJson = "{\"a\":1}",
            OutputFileName = "report.xlsx",
            CreatesDocumentClass = "ExcelReport",
            OutputFormat = Json2XlsxOutputFormat.XlsxAndWord,
            ExecutionState = new Json2XlsxPropertiesExecutionState(new Json2XlsxProperties()),
        };

        var clone = (Json2XlsxProperties)source.Clone();

        CloneAssertions.AssertAllCloneableFieldsCopied(source, clone);
        Assert.Same(source.ExecutionState, clone.ExecutionState);
    }

    [Fact]
    public void LoopStartProperties_Clone_CopiesEveryField()
    {
        var source = new LoopStartProperties
        {
            ListTitle = "items",
            LoopedListTemplate = "{item}",
            ExecutionState = new LoopStartPropertiesExecutionState(new LoopStartProperties()),
        };

        var clone = (LoopStartProperties)source.Clone();

        CloneAssertions.AssertAllCloneableFieldsCopied(source, clone);
        Assert.Same(source.ExecutionState, clone.ExecutionState);
    }

    [Fact]
    public void CalculateFormulaProperties_Clone_CopiesEveryField()
    {
        var source = new CalculateFormulaProperties
        {
            FormulaTemplate = "{a} + {b}",
            ExecutionState = new CalculateFormulaPropertiesExecutionState(new CalculateFormulaProperties()),
        };

        var clone = (CalculateFormulaProperties)source.Clone();

        CloneAssertions.AssertAllCloneableFieldsCopied(source, clone);
        Assert.Same(source.ExecutionState, clone.ExecutionState);
    }

    [Fact]
    public void DynamicChartProperties_Clone_CopiesEveryField()
    {
        var source = new DynamicChartProperties
        {
            DataSourceJson = "{\"rows\":[]}",
            ChartType = "bar",
            XAxisField = "month",
            YAxisField = "sales",
            SeriesGroupField = "region",
            ChartTitle = "Quarterly Sales",
            XAxisLabel = "Month",
            YAxisLabel = "Sales ($)",
            ColorScheme = "professional",
            ChartHeight = "600",
            ChartWidth = "800px",
            DataFilters = "[{\"field\":\"year\",\"op\":\"eq\",\"value\":\"2026\"}]",
            ShowLegend = false,
            ShowGridLines = false,
            EnableAnimations = false,
            Stacking = "normal",
            ExecutionState = new DynamicChartPropertiesExecutionState(new DynamicChartProperties()),
        };

        var clone = (DynamicChartProperties)source.Clone();

        CloneAssertions.AssertAllCloneableFieldsCopied(source, clone);
        Assert.Same(source.ExecutionState, clone.ExecutionState);
    }

    [Fact]
    public void WebApiCallProperties_Clone_CopiesEveryField()
    {
        var source = new WebApiCallProperties
        {
            UrlTemplate = "https://api.example.com/{endpoint}",
            RequestPayloadTemplate = "{\"q\":\"{query}\"}",
            HttpMethod = "POST",
            ContentType = "application/json",
            AcceptHeader = "application/xml",
            AdditionalHeaders = new Dictionary<string, string>
            {
                { "Authorization", "Bearer token" },
                { "X-Trace-Id", "abc123" },
            },
            DelayAfterRequestSeconds = 5,
            ExecutionState = new WebApiCallPropertiesExecutionState(new WebApiCallProperties()),
        };

        var clone = (WebApiCallProperties)source.Clone();

        CloneAssertions.AssertAllCloneableFieldsCopied(source, clone);
        Assert.NotSame(source.AdditionalHeaders, clone.AdditionalHeaders);
        Assert.Same(source.ExecutionState, clone.ExecutionState);
    }

    [Fact]
    public void RetrieveDataFromDocumentsProperties_Clone_CopiesEveryField()
    {
        var source = new RetrieveDataFromDocumentsProperties
        {
            ShouldUseOverviews = true,
            ExecutionState = new RetrieveDataFromDocumentsPropertiesExecutionState(
                new RetrieveDataFromDocumentsProperties()),
        };

        var clone = (RetrieveDataFromDocumentsProperties)source.Clone();

        CloneAssertions.AssertAllCloneableFieldsCopied(source, clone);
        Assert.Same(source.ExecutionState, clone.ExecutionState);
    }
}

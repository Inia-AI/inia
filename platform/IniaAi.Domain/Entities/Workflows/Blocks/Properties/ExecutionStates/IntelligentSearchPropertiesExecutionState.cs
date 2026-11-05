using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates.Abstractions;
using IniaAi.Common.Reflection;
using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;

/// <summary>
/// Execution state for IntelligentSearchProperties. Holds every templatable setting with its placeholders filled.
/// </summary>
public class IntelligentSearchPropertiesExecutionState : BlockPropertiesExecutionStateBase<IntelligentSearchPropertiesExecutionState>
{
    [JsonProperty(nameof(SearchTerm), NullValueHandling = NullValueHandling.Ignore)]
    public string? SearchTerm { get; set; }

    [JsonProperty(nameof(CreatesDocumentClass), NullValueHandling = NullValueHandling.Ignore)]
    public string? CreatesDocumentClass { get; set; }

    [JsonProperty(nameof(SearchType), NullValueHandling = NullValueHandling.Ignore)]
    public string? SearchType { get; set; }

    [JsonProperty(nameof(SummaryQuery), NullValueHandling = NullValueHandling.Ignore)]
    public string? SummaryQuery { get; set; }

    [SanitizePlaceholderValues(PlaceholderSanitization.Json)]
    [JsonProperty(nameof(SummarySchema), NullValueHandling = NullValueHandling.Ignore)]
    public string? SummarySchema { get; set; }

    [JsonProperty(nameof(Category), NullValueHandling = NullValueHandling.Ignore)]
    public string? Category { get; set; }

    [JsonProperty(nameof(StartPublishedDate), NullValueHandling = NullValueHandling.Ignore)]
    public string? StartPublishedDate { get; set; }

    [JsonProperty(nameof(EndPublishedDate), NullValueHandling = NullValueHandling.Ignore)]
    public string? EndPublishedDate { get; set; }

    [JsonProperty(nameof(LiveCrawlStrategy), NullValueHandling = NullValueHandling.Ignore)]
    public string? LiveCrawlStrategy { get; set; }

    [JsonProperty(nameof(IncludeDomains))]
    public List<string> IncludeDomains { get; set; } = [];

    [JsonProperty(nameof(ExcludeDomains))]
    public List<string> ExcludeDomains { get; set; } = [];

    [JsonProperty(nameof(SubpageTarget))]
    public List<string> SubpageTarget { get; set; } = [];

    [JsonConstructor]
    [System.Text.Json.Serialization.JsonConstructor]
    internal IntelligentSearchPropertiesExecutionState()
    {
    }

    public IntelligentSearchPropertiesExecutionState(IntelligentSearchProperties source)
    {
        SearchTerm = source.SearchTermTemplate;
        CreatesDocumentClass = source.CreatesDocumentClass;
        SearchType = source.SearchType;
        SummaryQuery = source.SummaryQuery;
        SummarySchema = source.SummarySchema;
        Category = source.Category;
        StartPublishedDate = source.StartPublishedDate;
        EndPublishedDate = source.EndPublishedDate;
        LiveCrawlStrategy = source.LiveCrawlStrategy;
        IncludeDomains = [.. source.IncludeDomains];
        ExcludeDomains = [.. source.ExcludeDomains];
        SubpageTarget = [.. source.SubpageTarget];
    }

    public bool ShouldSerializeIncludeDomains()
    {
        return IncludeDomains.Count > 0;
    }

    public bool ShouldSerializeExcludeDomains()
    {
        return ExcludeDomains.Count > 0;
    }

    public bool ShouldSerializeSubpageTarget()
    {
        return SubpageTarget.Count > 0;
    }
}

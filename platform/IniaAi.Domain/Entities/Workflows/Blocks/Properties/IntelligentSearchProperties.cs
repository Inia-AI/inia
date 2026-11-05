using IniaAi.Common.Reflection;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties;

/// <summary>
/// Properties for Exa AI-powered intelligent search block.
/// </summary>
public class IntelligentSearchProperties : BlockProperties, IProducesDocumentBlockProperties
{
    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the search term. It can contain placeholders that get
    /// replaced at execution time. Use
    /// <see cref="IntelligentSearchPropertiesExecutionState.SearchTerm"/>
    /// for the actual search term after replacements.
    /// </summary>
    [JsonProperty(nameof(SearchTermTemplate))]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? SearchTermTemplate { get; set; }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the created document class. It can contain placeholders
    /// that get replaced at execution time. Use
    /// <see cref="IntelligentSearchPropertiesExecutionState.CreatesDocumentClass"/>
    /// for the actual document class after replacements.
    /// </summary>
    [JsonProperty(nameof(CreatesDocumentClass), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? CreatesDocumentClass { get; set; }

    [JsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    [NotMapped]
    public bool RequiresDocumentClass => false;

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the search type. It can contain placeholders that get
    /// replaced at execution time. Use
    /// <see cref="IntelligentSearchPropertiesExecutionState.SearchType"/>
    /// for the actual search type after replacements.
    /// </summary>
    /// <remarks>
    /// Valid values: auto, fast, keyword, neural.
    /// </remarks>
    [JsonProperty(nameof(SearchType))]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string SearchType { get; set; } = "auto";

    /// <summary>
    /// Number of results to return (1-100).
    /// </summary>
    [JsonProperty(nameof(NumResults))]
    public int NumResults { get; set; } = 10;

    /// <summary>
    /// Whether to retrieve page text content.
    /// </summary>
    [JsonProperty(nameof(TextEnabled), NullValueHandling = NullValueHandling.Ignore)]
    public bool? TextEnabled { get; set; }

    /// <summary>
    /// Whether to include HTML tags in text content.
    /// </summary>
    [JsonProperty(nameof(IncludeHtmlTags), NullValueHandling = NullValueHandling.Ignore)]
    public bool? IncludeHtmlTags { get; set; }

    /// <summary>
    /// Maximum characters to retrieve from each page.
    /// </summary>
    [JsonProperty(nameof(MaxCharacters), NullValueHandling = NullValueHandling.Ignore)]
    public int? MaxCharacters { get; set; }

    /// <summary>
    /// Whether to retrieve highlights from pages.
    /// </summary>
    [JsonProperty(nameof(HighlightsEnabled), NullValueHandling = NullValueHandling.Ignore)]
    public bool? HighlightsEnabled { get; set; }

    /// <summary>
    /// Whether to generate AI summaries.
    /// </summary>
    [JsonProperty(nameof(SummaryEnabled), NullValueHandling = NullValueHandling.Ignore)]
    public bool? SummaryEnabled { get; set; }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the AI-summary query. It can contain placeholders that
    /// get replaced at execution time. Use
    /// <see cref="IntelligentSearchPropertiesExecutionState.SummaryQuery"/>
    /// for the actual summary query after replacements.
    /// </summary>
    [JsonProperty(nameof(SummaryQuery), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? SummaryQuery { get; set; }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the summary schema. It can contain placeholders that
    /// get replaced at execution time. Use
    /// <see cref="IntelligentSearchPropertiesExecutionState.SummarySchema"/>
    /// for the actual summary schema after replacements.
    /// </summary>
    [JsonProperty(nameof(SummarySchema), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? SummarySchema { get; set; }

    /// <summary>
    /// Domains to exclude from results.
    /// </summary>
    [JsonProperty(nameof(ExcludeDomains))]
    public List<string> ExcludeDomains { get; set; } = [];

    public bool ShouldSerializeExcludeDomains()
    {
        return ExcludeDomains.Count > 0;
    }

    /// <summary>
    /// Domains to include in results.
    /// </summary>
    [JsonProperty(nameof(IncludeDomains))]
    public List<string> IncludeDomains { get; set; } = [];

    public bool ShouldSerializeIncludeDomains()
    {
        return IncludeDomains.Count > 0;
    }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the category filter. It can contain placeholders that
    /// get replaced at execution time. Use
    /// <see cref="IntelligentSearchPropertiesExecutionState.Category"/> for
    /// the actual category after replacements.
    /// </summary>
    /// <remarks>
    /// Example values: news, research, company.
    /// </remarks>
    [JsonProperty(nameof(Category), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? Category { get; set; }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the start published-date filter. It can contain
    /// placeholders that get replaced at execution time. Use
    /// <see cref="IntelligentSearchPropertiesExecutionState.StartPublishedDate"/>
    /// for the actual start published date after replacements.
    /// </summary>
    /// <remarks>
    /// ISO 8601 format.
    /// </remarks>
    [JsonProperty(nameof(StartPublishedDate), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? StartPublishedDate { get; set; }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the end published-date filter. It can contain
    /// placeholders that get replaced at execution time. Use
    /// <see cref="IntelligentSearchPropertiesExecutionState.EndPublishedDate"/>
    /// for the actual end published date after replacements.
    /// </summary>
    /// <remarks>
    /// ISO 8601 format.
    /// </remarks>
    [JsonProperty(nameof(EndPublishedDate), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? EndPublishedDate { get; set; }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the live-crawl strategy. It can contain placeholders
    /// that get replaced at execution time. Use
    /// <see cref="IntelligentSearchPropertiesExecutionState.LiveCrawlStrategy"/>
    /// for the actual live-crawl strategy after replacements.
    /// </summary>
    /// <remarks>
    /// Valid values: never, fallback, always, preferred.
    /// </remarks>
    [JsonProperty(nameof(LiveCrawlStrategy), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? LiveCrawlStrategy { get; set; }

    /// <summary>
    /// Timeout in seconds for live crawl operations.
    /// </summary>
    [JsonProperty(nameof(LiveCrawlTimeout), NullValueHandling = NullValueHandling.Ignore)]
    public int? LiveCrawlTimeout { get; set; }

    /// <summary>
    /// Number of subpages to crawl per result (1-10).
    /// </summary>
    [JsonProperty(nameof(Subpages), NullValueHandling = NullValueHandling.Ignore)]
    public int? Subpages { get; set; }

    /// <summary>
    /// Keywords to target when crawling subpages.
    /// </summary>
    [JsonProperty(nameof(SubpageTarget))]
    public List<string> SubpageTarget { get; set; } = [];

    public bool ShouldSerializeSubpageTarget()
    {
        return SubpageTarget.Count > 0;
    }

    /// <summary>
    /// Maximum number of pages a single search result may produce when paginated.
    /// Results exceeding this limit are excluded from both text output and documents.
    /// Null means use the strategy default (100).
    /// </summary>
    [JsonProperty(nameof(MaxPagesPerResult), NullValueHandling = NullValueHandling.Ignore)]
    public int? MaxPagesPerResult { get; set; }

    [NotMapped]
    [JsonProperty(nameof(ExecutionState), NullValueHandling = NullValueHandling.Ignore)]
    public IntelligentSearchPropertiesExecutionState? ExecutionState { get; set; }

    public IntelligentSearchProperties()
    {
        Type = BlockTypes.IntelligentSearch;
    }

    public override BlockProperties Clone()
    {
        var clone = new IntelligentSearchProperties();
        CloneHelper.CopyScalars(this, clone);
        return clone;
    }

    public override void SetExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState = new IntelligentSearchPropertiesExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void UpdateExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState ??= new IntelligentSearchPropertiesExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void ClearExecutionState()
    {
        ExecutionState = null;
    }
}

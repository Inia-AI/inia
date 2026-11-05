using IniaAi.Domain.Entities.SelfServe;
using IniaAi.Domain.Entities.WorkflowExecutions;
using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Workflows;

/// <summary>
/// Strongly-typed configuration for workflow settings.
/// Provides extensible structure for various workflow configurations.
/// </summary>
public class WorkflowConfiguration
{
    /// <summary>
    /// Grid layout configuration for report display.
    /// </summary>
    [JsonProperty(nameof(GridLayout), NullValueHandling = NullValueHandling.Ignore)]
    public GridLayoutSettings? GridLayout { get; set; }

    /// <summary>
    /// Report layout configuration for Word/PDF export.
    /// Defines page orientation and future print-specific settings.
    /// </summary>
    [JsonProperty(nameof(ReportLayout), NullValueHandling = NullValueHandling.Ignore)]
    public ReportLayout? ReportLayout { get; set; }

    /// <summary>
    /// Custom settings for extensibility.
    /// Can store any additional configuration as key-value pairs.
    /// </summary>
    [JsonProperty(nameof(CustomSettings), NullValueHandling = NullValueHandling.Ignore)]
    public Dictionary<string, object>? CustomSettings { get; set; }

    /// <summary>
    /// The workflow's default page overview prompts, used for a document class without its own.
    /// </summary>
    [JsonProperty(nameof(PageOverviews), NullValueHandling = NullValueHandling.Ignore)]
    public PageOverviewPrompts? PageOverviews { get; set; }

    public bool ShouldSerializePageOverviews()
    {
        return PageOverviews is { IsEmpty: false };
    }

    /// <summary>
    /// The credit cost and the limits of a self-serve run of this workflow type; absent on a type that
    /// self-serve customers do not run.
    /// </summary>
    [JsonProperty(nameof(SelfServe), NullValueHandling = NullValueHandling.Ignore)]
    public SelfServeWorkflowSettings? SelfServe { get; set; }

    public bool ShouldSerializeSelfServe()
    {
        return SelfServe is { IsEmpty: false };
    }

    /// <summary>
    /// Version of the configuration format for future compatibility.
    /// </summary>
    [JsonProperty(nameof(Version))]
    public string Version { get; set; } = "1.0";

    /// <summary>
    /// Whether nothing is set beyond the format version, so the workflow could carry no
    /// configuration at all and save to the same bytes.
    /// </summary>
    public bool IsUnset()
    {
        return GridLayout is null
            && ReportLayout is null
            && CustomSettings is null
            && !ShouldSerializePageOverviews()
            && !ShouldSerializeSelfServe()
            && Version == new WorkflowConfiguration().Version;
    }

    public WorkflowConfiguration Clone()
    {
        return new WorkflowConfiguration
        {
            Version = Version,
            GridLayout = GridLayout is null ? null : new GridLayoutSettings
            {
                Mode = GridLayout.Mode,
                Columns = GridLayout.Columns,
                Elements = GridLayout.Elements?.Select(e => new GridElementPosition
                {
                    BlockOrder = e.BlockOrder,
                    Row = e.Row,
                    Column = e.Column,
                    RowSpan = e.RowSpan,
                    ColumnSpan = e.ColumnSpan,
                }).ToList(),
            },
            ReportLayout = ReportLayout is null ? null : new ReportLayout
            {
                Orientation = ReportLayout.Orientation,
                PageSize = ReportLayout.PageSize,
                Version = ReportLayout.Version,
            },
            CustomSettings = CustomSettings is null ? null : new Dictionary<string, object>(CustomSettings),
            PageOverviews = PageOverviews?.Clone(),
            SelfServe = SelfServe?.Clone(),
        };
    }
}

/// <summary>
/// Grid layout configuration for positioning workflow output elements.
/// </summary>
public class GridLayoutSettings
{
    /// <summary>
    /// Layout mode: "sequential" or "grid".
    /// </summary>
    [JsonProperty(nameof(Mode))]
    public string Mode { get; set; } = "sequential";

    /// <summary>
    /// Number of columns in the grid (1-12).
    /// </summary>
    [JsonProperty(nameof(Columns))]
    public int Columns { get; set; } = 2;

    /// <summary>
    /// Grid positioning for each element.
    /// Reuses GridElementPosition from WorkflowExecutions namespace.
    /// </summary>
    [JsonProperty(nameof(Elements), NullValueHandling = NullValueHandling.Ignore)]
    public List<GridElementPosition>? Elements { get; set; }
}
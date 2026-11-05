using IniaAi.Common.Reflection;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties;

/// <summary>
/// Properties for the Peer Financials block: matches each peer counterparty's stored
/// financial periods to the target's reporting period and emits the aligned, converted
/// figures as JSON. All period-selection policy lives here as configuration — the
/// platform code carries no period-kind literals.
/// </summary>
public class PeerFinancialsProperties : BlockProperties
{
    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a template
    /// for the target's available periods. It can contain placeholders that get replaced
    /// at execution time. Use
    /// <see cref="PeerFinancialsPropertiesExecutionState.TargetPeriodsJson"/> for the
    /// resolved value.
    /// </summary>
    /// <remarks>
    /// JSON object mapping period kind to that period's end date, e.g.
    /// <c>{"FY": "2024-12-31", "LTM": "2025-06-30"}</c>. Omit a kind the target
    /// cannot offer. Dates accept <c>yyyy-MM-dd</c> or <c>yyyy-MM</c>.
    /// </remarks>
    [JsonProperty(nameof(TargetPeriodsJson))]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? TargetPeriodsJson { get; set; }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a template
    /// for the target's display currency. Use
    /// <see cref="PeerFinancialsPropertiesExecutionState.TargetCurrency"/> for the
    /// resolved value.
    /// </summary>
    [JsonProperty(nameof(TargetCurrency))]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? TargetCurrency { get; set; }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a template
    /// for the target's display units (e.g. "Millions"). Use
    /// <see cref="PeerFinancialsPropertiesExecutionState.TargetUnits"/> for the
    /// resolved value.
    /// </summary>
    [JsonProperty(nameof(TargetUnits))]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? TargetUnits { get; set; }

    /// <summary>
    /// How period kinds are reconciled across the target and its peers.
    /// </summary>
    [JsonProperty(nameof(BasisMode))]
    public PeerBasisMode BasisMode { get; set; } = PeerBasisMode.CommonBasis;

    /// <summary>
    /// Ordered period-kind preferences. The kinds are opaque labels matched against the
    /// keys of <see cref="TargetPeriodsJson"/> and the <c>type</c> of each stored peer
    /// period; a kind not listed here is never a candidate.
    /// </summary>
    [JsonProperty(nameof(PeriodPreferences))]
    public List<PeerPeriodPreference> PeriodPreferences { get; set; } = [];

    /// <summary>
    /// Metric keys to emit per peer, in output order. Keys are matched against the
    /// stored period's metrics; a missing key emits null.
    /// </summary>
    [JsonProperty(nameof(MetricKeys))]
    public List<string> MetricKeys { get; set; } = [];

    /// <summary>
    /// Subset of <see cref="MetricKeys"/> that are ratios: emitted as stored, exempt
    /// from currency and unit conversion.
    /// </summary>
    [JsonProperty(nameof(RatioMetricKeys))]
    public List<string> RatioMetricKeys { get; set; } = [];

    /// <summary>
    /// JSON property under which each peer counterparty's stored financial data lives
    /// inside <c>Counterparty.LastFinancialAnalysisResult</c>. Empty (the default) reads
    /// the document root — the shape the Json2Xlsx Extract block stores directly.
    /// The node carries <c>period_kind</c> / <c>period_end</c> / <c>period_label</c>
    /// objects keyed by period column, and one object per metric key with the same keys.
    /// </summary>
    [JsonProperty(nameof(PeerDataPath))]
    public string PeerDataPath { get; set; } = string.Empty;

    [NotMapped]
    [JsonProperty(nameof(ExecutionState), NullValueHandling = NullValueHandling.Ignore)]
    public PeerFinancialsPropertiesExecutionState? ExecutionState { get; set; }

    public PeerFinancialsProperties()
    {
        Type = BlockTypes.PeerFinancials;
    }

    public bool ShouldSerializeBasisMode() => BasisMode != PeerBasisMode.CommonBasis;

    public override BlockProperties Clone()
    {
        var clone = new PeerFinancialsProperties();
        CloneHelper.CopyScalars(this, clone);
        clone.PeriodPreferences = [.. PeriodPreferences.Select(p => p.Clone())];
        clone.MetricKeys = [.. MetricKeys];
        clone.RatioMetricKeys = [.. RatioMetricKeys];
        return clone;
    }

    public override void SetExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState = new PeerFinancialsPropertiesExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void UpdateExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState ??= new PeerFinancialsPropertiesExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void ClearExecutionState()
    {
        ExecutionState = null;
    }
}

/// <summary>
/// One entry of the ordered period-kind preference list.
/// </summary>
public class PeerPeriodPreference
{
    /// <summary>
    /// Opaque period-kind label (e.g. "LTM", "FY") matched against stored period types.
    /// </summary>
    [JsonProperty(nameof(Type))]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// When set, a stored period of this kind qualifies only if its end date is within
    /// this many months (either direction) of the target's end date for the same kind.
    /// When null, any period ending on or before the target's end date qualifies.
    /// </summary>
    [JsonProperty(nameof(ToleranceMonths), NullValueHandling = NullValueHandling.Ignore)]
    public int? ToleranceMonths { get; set; }

    public PeerPeriodPreference Clone() => new() { Type = Type, ToleranceMonths = ToleranceMonths };
}

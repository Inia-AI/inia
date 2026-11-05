using IniaAi.Common.Reflection;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Enums;
using Newtonsoft.Json;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties;

/// <summary>
/// Properties for exchange rate lookup block.
/// </summary>
public class ExchangeRateProperties : BlockProperties
{
    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the source currency code. It can contain placeholders
    /// that get replaced at execution time. Use
    /// <see cref="ExchangeRatePropertiesExecutionState.FromCurrency"/> for
    /// the actual source currency after replacements.
    /// </summary>
    /// <remarks>
    /// Three-letter ISO 4217 currency code (e.g., USD, EUR, GBP).
    /// </remarks>
    [JsonProperty(nameof(FromCurrency))]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? FromCurrency { get; set; }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the target currency code. It can contain placeholders
    /// that get replaced at execution time. Use
    /// <see cref="ExchangeRatePropertiesExecutionState.ToCurrency"/> for the
    /// actual target currency after replacements.
    /// </summary>
    /// <remarks>
    /// Three-letter ISO 4217 currency code (e.g., USD, EUR, GBP).
    /// </remarks>
    [JsonProperty(nameof(ToCurrency))]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? ToCurrency { get; set; }

    /// <summary>
    /// Optional additional target currencies for multi-target conversion. Each entry is a
    /// template that can contain placeholders resolved at execution time; use
    /// <see cref="ExchangeRatePropertiesExecutionState.ToCurrencies"/> for the resolved values.
    /// When null/empty (the default), the block behaves exactly as a single-target conversion
    /// to <see cref="ToCurrency"/>. The effective target set is <see cref="ToCurrency"/> followed
    /// by these, de-duplicated case-insensitively.
    /// </summary>
    /// <remarks>
    /// Three-letter ISO 4217 currency codes (e.g., USD, EUR, GBP).
    /// </remarks>
    [JsonProperty(nameof(ToCurrencies), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public List<string>? ToCurrencies { get; set; }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the exchange date. It can contain placeholders that get
    /// replaced at execution time. Use
    /// <see cref="ExchangeRatePropertiesExecutionState.ExchangeDate"/> for
    /// the actual exchange date after replacements.
    /// </summary>
    /// <remarks>
    /// ISO 8601 format. Used when <see cref="UseCurrentDate"/> is false.
    /// </remarks>
    [JsonProperty(nameof(ExchangeDate), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? ExchangeDate { get; set; }

    /// <summary>
    /// Whether to use the current date instead of a specific date.
    /// </summary>
    [JsonProperty(nameof(UseCurrentDate))]
    public bool UseCurrentDate { get; set; } = true;

    /// <summary>
    /// Operation mode (e.g., GetRate, Convert).
    /// </summary>
    [JsonProperty(nameof(OperationMode))]
    public ExchangeRateOperationMode OperationMode { get; set; } = ExchangeRateOperationMode.GetRate;

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the value(s) to convert. It can contain placeholders
    /// that get replaced at execution time. Use
    /// <see cref="ExchangeRatePropertiesExecutionState.InputValue"/> for the
    /// actual input value after replacements.
    /// </summary>
    /// <remarks>
    /// Accepts a single number or JSON with currency-keyed fields.
    /// </remarks>
    [JsonProperty(nameof(InputValue))]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? InputValue { get; set; }

    [NotMapped]
    [JsonProperty(nameof(ExecutionState), NullValueHandling = NullValueHandling.Ignore)]
    public ExchangeRatePropertiesExecutionState? ExecutionState { get; set; }

    public ExchangeRateProperties()
    {
        Type = BlockTypes.ExchangeRate;
    }

    public override BlockProperties Clone()
    {
        var clone = new ExchangeRateProperties();
        CloneHelper.CopyScalars(this, clone);
        return clone;
    }

    public override void SetExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState = new ExchangeRatePropertiesExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void UpdateExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState ??= new ExchangeRatePropertiesExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void ClearExecutionState()
    {
        ExecutionState = null;
    }
}

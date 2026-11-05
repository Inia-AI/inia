using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates.Abstractions;
using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;

/// <summary>
/// Execution state for ExchangeRateProperties. Holds resolved FromCurrency and ToCurrency.
/// </summary>
public class ExchangeRatePropertiesExecutionState : BlockPropertiesExecutionStateBase<ExchangeRatePropertiesExecutionState>
{
    [JsonProperty(nameof(FromCurrency), NullValueHandling = NullValueHandling.Ignore)]
    public string? FromCurrency { get; set; }

    [JsonProperty(nameof(ToCurrency), NullValueHandling = NullValueHandling.Ignore)]
    public string? ToCurrency { get; set; }

    [JsonProperty(nameof(ToCurrencies), NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? ToCurrencies { get; set; }

    [JsonProperty(nameof(ExchangeDate), NullValueHandling = NullValueHandling.Ignore)]
    public string? ExchangeDate { get; set; }

    [JsonProperty(nameof(InputValue), NullValueHandling = NullValueHandling.Ignore)]
    public string? InputValue { get; set; }

    [JsonConstructor]
    [System.Text.Json.Serialization.JsonConstructor]
    internal ExchangeRatePropertiesExecutionState()
    {
    }

    public ExchangeRatePropertiesExecutionState(ExchangeRateProperties source)
    {
        FromCurrency = source.FromCurrency;
        ToCurrency = source.ToCurrency;
        ToCurrencies = source.ToCurrencies is null ? null : [.. source.ToCurrencies];
        ExchangeDate = source.ExchangeDate;
        InputValue = source.InputValue;
    }
}

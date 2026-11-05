using IniaAi.Common.Serialization;
using Newtonsoft.Json;
using System.Text.Json;

namespace IniaAi.Core.Dto.ExchangeRate;

public class ExchangeRateResponse : JsonSerializable<ExchangeRateResponse>
{
    [JsonProperty("result")]
    public string? Result { get; set; }

    [JsonProperty("documentation")]
    public string? Documentation { get; set; }

    [JsonProperty("terms_of_use")]
    public string? TermsOfUse { get; set; }

    [JsonProperty("year")]
    public int Year { get; set; }

    [JsonProperty("month")]
    public int Month { get; set; }

    [JsonProperty("day")]
    public int Day { get; set; }

    [JsonProperty("base_code")]
    public string? BaseCode { get; set; }

    [JsonProperty("conversion_rates")]
    public Dictionary<string, decimal>? ConversionRates { get; set; }

    [JsonProperty("error-type")]
    public string? ErrorType { get; set; }
}

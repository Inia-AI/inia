using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.SelfServe;

/// <summary>
/// What a self-serve customer pays for a run of the workflow type and the limits on it. It sits in
/// the template's configuration, so export and import carry it.
/// </summary>
public class SelfServeWorkflowSettings
{
    [JsonProperty(nameof(Enabled))]
    public bool Enabled { get; set; }

    [JsonProperty(nameof(CreditCost), NullValueHandling = NullValueHandling.Ignore)]
    public int? CreditCost { get; set; }

    [JsonProperty(nameof(MaxDocuments), NullValueHandling = NullValueHandling.Ignore)]
    public int? MaxDocuments { get; set; }

    [JsonProperty(nameof(MaxPages), NullValueHandling = NullValueHandling.Ignore)]
    public int? MaxPages { get; set; }

    [JsonProperty(nameof(MaxTokens), NullValueHandling = NullValueHandling.Ignore)]
    public long? MaxTokens { get; set; }

    /// <summary>Whether new runs of the type are refused while it stays offered, so customers learn it will come back.</summary>
    [JsonProperty(nameof(Paused), DefaultValueHandling = DefaultValueHandling.Ignore)]
    public bool Paused { get; set; }

    [JsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => !Enabled && !Paused && CreditCost is null && MaxDocuments is null && MaxPages is null && MaxTokens is null;

    public SelfServeWorkflowSettings Clone()
    {
        return (SelfServeWorkflowSettings)MemberwiseClone();
    }
}

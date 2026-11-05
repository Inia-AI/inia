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

    [JsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => !Enabled && CreditCost is null && MaxDocuments is null && MaxPages is null && MaxTokens is null;

    public SelfServeWorkflowSettings Clone()
    {
        return (SelfServeWorkflowSettings)MemberwiseClone();
    }
}

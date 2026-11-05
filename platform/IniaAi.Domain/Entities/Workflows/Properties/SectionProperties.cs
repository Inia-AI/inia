using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Workflows.Properties;

public class SectionProperties
{
    [JsonProperty(nameof(IsSeparator))]
    public bool IsSeparator { get; set; }

    [JsonProperty(nameof(ExcludeFromWordExport))]
    public bool ExcludeFromWordExport { get; set; }

    public SectionProperties Clone()
    {
        string json = JsonConvert.SerializeObject(this);
        return JsonConvert.DeserializeObject<SectionProperties>(json)
            ?? new SectionProperties();
    }

    public bool IsDefault()
    {
        string thisJson = JsonConvert.SerializeObject(this);
        string defaultJson = JsonConvert.SerializeObject(new SectionProperties());
        return thisJson == defaultJson;
    }
}

using IniaAi.Domain.Entities.Citations.Sources;
using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Citations;

public class Citation
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("sources")]
    public List<BaseSource> Sources { get; set; } = [];

    [JsonProperty("isNumerical")]
    public bool IsNumerical { get; set; }
}

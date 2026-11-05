using IniaAi.Domain.Entities.Citations.Sources;
using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Citations;

/// <summary>
/// Every page and link that one block, or any block upstream of it, cited during the run: the
/// list a reader follows when a citation points to that block. A report paragraph stores it once
/// per block its citations reach, not once per citation.
/// </summary>
public class BlockCitedSources
{
    [JsonProperty("blockId")]
    public string BlockId { get; set; } = string.Empty;

    [JsonProperty("sources")]
    public List<BaseSource> Sources { get; set; } = [];
}

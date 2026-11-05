using IniaAi.Common.Serialization;
using Newtonsoft.Json;

namespace IniaAi.Core.Workflows.Outputs.TableOfContents;

public class TableOfContentsList : JsonSerializable<TableOfContentsList>
{
    [JsonProperty(nameof(Sections))]
    public List<TableOfContentsEntry> Sections { get; set; } = [];
}

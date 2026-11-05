using IniaAi.Common.Serialization;
using Newtonsoft.Json;

namespace IniaAi.Core.Workflows.Outputs.TableOfContents;

public class TableOfContentsEntry : JsonSerializable<TableOfContentsEntry>
{
    [JsonProperty(nameof(Title))]
    public string Title { get; set; } = string.Empty;

    [JsonProperty(nameof(StartPage))]
    public int? StartPage { get; set; }

    [JsonProperty(nameof(EndPage))]
    public int? EndPage { get; set; }

    public bool ShouldSerializeEndPage()
    {
        return EndPage > 0;
    }
}

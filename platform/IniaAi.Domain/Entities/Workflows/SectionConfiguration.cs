using IniaAi.Domain.Entities.WorkflowExecutions;
using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Workflows;

public class SectionConfiguration
{
    [JsonProperty(nameof(GridLayout), NullValueHandling = NullValueHandling.Ignore)]
    public GridLayoutSettings? GridLayout { get; set; }

    [JsonProperty(nameof(Version))]
    public string Version { get; set; } = "1.0";

    public SectionConfiguration Clone()
    {
        return new SectionConfiguration
        {
            Version = Version,
            GridLayout = GridLayout is null ? null : new GridLayoutSettings
            {
                Mode = GridLayout.Mode,
                Columns = GridLayout.Columns,
                Elements = GridLayout.Elements?.Select(e => new GridElementPosition
                {
                    BlockOrder = e.BlockOrder,
                    Row = e.Row,
                    Column = e.Column,
                    RowSpan = e.RowSpan,
                    ColumnSpan = e.ColumnSpan,
                }).ToList(),
            },
        };
    }
}

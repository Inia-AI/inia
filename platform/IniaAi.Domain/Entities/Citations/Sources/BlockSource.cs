using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Citations.Sources;

public class BlockSource : BaseSource
{
    public override CitationSourceType Type => CitationSourceType.Block;

    public override string SourceKey => $"block:{BlockId}";

    [JsonProperty(nameof(BlockId))]
    public string BlockId { get; set; } = string.Empty;

    [JsonProperty(nameof(BlockTitle))]
    public string? BlockTitle { get; set; }

    [JsonProperty(nameof(UpstreamSources))]
    public List<BaseSource> UpstreamSources { get; set; } = [];

    /// <summary>
    /// The pages and links from the block's cited-source list whose excerpt states the fact this
    /// citation marks, each excerpted around that fact. Only a citation's own block sources carry
    /// them; a block source inside the run's annotation graph never does.
    /// </summary>
    [JsonProperty(nameof(MatchingSources))]
    public List<BaseSource> MatchingSources { get; set; } = [];

    public bool ShouldSerializeMatchingSources()
    {
        return MatchingSources.Count > 0;
    }
}

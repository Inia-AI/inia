using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Citations.Sources;

public class DocumentSource : BaseSource
{
    public override CitationSourceType Type => CitationSourceType.Document;

    public override string SourceKey => PageId.Length > 0
        ? $"doc:{PageId}"
        : $"doc:{DocumentId}:{DocumentName}:{CitedText}";

    [JsonProperty(nameof(DocumentId))]
    public string DocumentId { get; set; } = string.Empty;

    [JsonProperty(nameof(PageId))]
    public string PageId { get; set; } = string.Empty;

    [JsonProperty(nameof(PageNumber))]
    public int PageNumber { get; set; }

    [JsonProperty(nameof(FilePageNumber))]
    public int FilePageNumber { get; set; }

    [JsonProperty(nameof(DocumentName))]
    public string? DocumentName { get; set; }

    [JsonProperty(nameof(Unverified))]
    public bool Unverified { get; set; }
}

using IniaAi.Domain.Common;
using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.DocumentAnalysis;

/// <summary>
/// Prompts that read a spreadsheet before it is cut into pages: the sheet layout, then the header
/// rows for each layout. The newest row wins at read time.
/// </summary>
public class ExcelAnalysisPrompts : Entity<ExcelAnalysisPrompts>
{
    [JsonProperty]
    public string? LayoutAnalysisSystemPrompt { get; set; }

    [JsonProperty]
    public string? LayoutAnalysisUserPrompt { get; set; }

    [JsonProperty]
    public string? HeaderDatabaseSystemPrompt { get; set; }

    [JsonProperty]
    public string? HeaderDatabaseUserPrompt { get; set; }

    [JsonProperty]
    public string? HeaderMultiNoSharedSystemPrompt { get; set; }

    [JsonProperty]
    public string? HeaderMultiNoSharedUserPrompt { get; set; }

    [JsonProperty]
    public string? HeaderMultiSharedSystemPrompt { get; set; }

    [JsonProperty]
    public string? HeaderMultiSharedUserPrompt { get; set; }

    public void CopyTo(ExcelAnalysisPrompts prompts)
    {
        prompts.LayoutAnalysisSystemPrompt = LayoutAnalysisSystemPrompt;
        prompts.LayoutAnalysisUserPrompt = LayoutAnalysisUserPrompt;
        prompts.HeaderDatabaseSystemPrompt = HeaderDatabaseSystemPrompt;
        prompts.HeaderDatabaseUserPrompt = HeaderDatabaseUserPrompt;
        prompts.HeaderMultiNoSharedSystemPrompt = HeaderMultiNoSharedSystemPrompt;
        prompts.HeaderMultiNoSharedUserPrompt = HeaderMultiNoSharedUserPrompt;
        prompts.HeaderMultiSharedSystemPrompt = HeaderMultiSharedSystemPrompt;
        prompts.HeaderMultiSharedUserPrompt = HeaderMultiSharedUserPrompt;
    }
}

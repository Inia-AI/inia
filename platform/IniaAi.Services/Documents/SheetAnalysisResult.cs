using IniaAi.Common.Serialization;

namespace IniaAi.Services.Documents;

public class SheetAnalysisResult : JsonSerializable<SheetAnalysisResult>
{
    public WorksheetLayoutTypes WorksheetLayoutType { get; set; } = WorksheetLayoutTypes.Unidentified;

    public string? HeaderRows { get; set; }

    public List<string> SharedColumns { get; set; } = [];

    public List<string> TableColumns { get; set; } = [];
}

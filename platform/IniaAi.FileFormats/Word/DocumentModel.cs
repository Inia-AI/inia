using IniaAi.Domain.Entities.WorkflowExecutions;

namespace IniaAi.FileFormats.Word;

public interface IDocumentElement
{
    string? Title { get; }
    string? Subtitle { get; }
    string? Content { get; }
    DocumentElementType Type { get; }
    int Order { get; }
    IReadOnlyList<IDocumentElement> Children { get; }
    IDictionary<string, string?> Metadata { get; }
    IReadOnlyList<WorkflowOutputData> OutputData { get; }
}

public class DocumentElement : IDocumentElement
{
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public string? Content { get; set; }
    public DocumentElementType Type { get; set; }
    public int Order { get; set; } = 0;
    public List<IDocumentElement> Children { get; set; } = [];
    public IDictionary<string, string?> Metadata { get; set; } = new Dictionary<string, string?>();
    public List<WorkflowOutputData> OutputData { get; set; } = [];

    IReadOnlyList<IDocumentElement> IDocumentElement.Children => Children;
    IReadOnlyList<WorkflowOutputData> IDocumentElement.OutputData => OutputData;
}

public static class DocumentMetadataKeys
{
    public const string COUNTERPARTY = "Counterparty";
    public const string ANALYST_NAME = "AnalystName";
}

public enum DocumentElementType
{
    Document,
    Section,
    Paragraph,
    Divider,
    Table,
    Chart
}

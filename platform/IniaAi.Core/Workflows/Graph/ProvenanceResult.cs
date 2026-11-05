using System.Text.Json.Serialization;

namespace IniaAi.Core.Workflows.Graph;

/// <summary>
/// Result of a provenance query containing the AI-generated answer.
/// </summary>
public class ProvenanceResult
{
    public string Answer { get; set; } = string.Empty;
}

/// <summary>
/// Internal data structure containing provenance graph data (nodes and outputs).
/// Used internally for LLM processing, not returned in API responses.
/// </summary>
internal class ProvenanceData
{
    public string Target { get; set; } = string.Empty;
    public int Depth { get; set; }
    public List<ProvenanceNode> Nodes { get; set; } = [];
    public List<NodeOutput> NodeOutputs { get; set; } = [];
}

public class ProvenanceNode
{
    public string Key { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? Label { get; set; }
    public int? Order { get; set; }
    public string? DocumentClass { get; set; }
}

public class ProvenanceEdge
{
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string? Label { get; set; }
}

public class NodeOutput
{
    public string Key { get; set; } = string.Empty;
    public string? Preview { get; set; }

    [JsonIgnore]
    public int? FullLength { get; set; }

    public string? DocumentClass { get; set; }
}

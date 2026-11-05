namespace IniaAi.Core.Workflows.Graph;

/// <summary>
/// Node-kind string values shared by the graph builder and the scheduling-view builder. These are
/// the canonical <see cref="WorkflowGraphNode.Kind"/> values; keeping them in one place stops the
/// two builders drifting apart on a magic string.
/// </summary>
public static class WorkflowGraphNodeKinds
{
    public const string BLOCK = "block";
    public const string INPUT_VARIABLE = "input_variable";
    public const string DOCUMENT_INPUT = "document_input";
    public const string INPUT_SIGNATURE = "input_signature";
    public const string INPUT_SYSTEM = "input_system";
    public const string INPUT_LOOP = "input_loop";
    public const string PARAMETERIZED_SECTION = "parameterized_section";
    public const string UNKNOWN = "unknown";
}

namespace IniaAi.WebApp.Components.WorkflowEditor.Common;

public sealed class JsonIndentMemo
{
    private string? _output;
    private string _indented = string.Empty;

    public string Indent(string output)
    {
        if (!ReferenceEquals(output, _output))
        {
            _output = output;
            _indented = JsonIndenter.Indent(output);
        }

        return _indented;
    }
}

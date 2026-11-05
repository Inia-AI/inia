using System.Text;
using IniaAi.Common;
using IniaAi.Domain.OutputFormat;

namespace IniaAi.WebApp.Components.WorkflowEditor.Common;

public static class JsonIndenter
{
    private const string INDENT = "  ";
    private const char NEW_LINE = '\n';

    // An author edit sends the whole field back over the Blazor hub, whose MaximumReceiveMessageSize
    // is 5 MB (Program.cs). Indenting roughly doubles the text, so a larger output shows as stored.
    private const int MAX_INDENTED_OUTPUT_LENGTH = 1_000_000;

    public static string Indent(string output)
    {
        if (output.Length > MAX_INDENTED_OUTPUT_LENGTH || output.AsSpan().TrimStart() is not ['{' or '[' or '`', ..])
        {
            return output;
        }

        string trimmed = output.Trim();
        string json = LenientCleanup.Clean(trimmed);
        if (json is not ['{' or '[', ..] || json.IsValidJson() != true)
        {
            return output;
        }

        if (json == trimmed)
        {
            return Reindent(json);
        }

        string fenceOpener = trimmed[..trimmed.IndexOf(NEW_LINE)].TrimEnd();
        string fenceCloser = trimmed[(trimmed.LastIndexOf(NEW_LINE) + 1)..];
        return $"{fenceOpener}{NEW_LINE}{Reindent(json)}{NEW_LINE}{fenceCloser}";
    }

    // Utf8JsonWriter re-escapes every string and key through its encoder, so this re-emits the
    // source text and changes only the whitespace outside strings.
    private static string Reindent(string json)
    {
        var indented = new StringBuilder();
        int depth = 0;
        bool inString = false;

        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];
            if (inString)
            {
                _ = indented.Append(c);
                if (c == '\\')
                {
                    _ = indented.Append(json[++i]);
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    _ = indented.Append(c);
                    break;
                case '{':
                case '[':
                    int next = NextTokenIndex(json, i + 1);
                    if (json[next] is '}' or ']')
                    {
                        _ = indented.Append(c).Append(json[next]);
                        i = next;
                    }
                    else
                    {
                        _ = indented.Append(c);
                        AppendLine(indented, ++depth);
                    }

                    break;
                case '}':
                case ']':
                    AppendLine(indented, --depth);
                    _ = indented.Append(c);
                    break;
                case ',':
                    _ = indented.Append(c);
                    AppendLine(indented, depth);
                    break;
                case ':':
                    _ = indented.Append(": ");
                    break;
                default:
                    if (!IsJsonWhitespace(c))
                    {
                        _ = indented.Append(c);
                    }

                    break;
            }
        }

        return indented.ToString();
    }

    private static bool IsJsonWhitespace(char c)
    {
        return c is ' ' or '\t' or '\r' or '\n';
    }

    private static int NextTokenIndex(string json, int start)
    {
        int index = start;
        while (IsJsonWhitespace(json[index]))
        {
            index++;
        }

        return index;
    }

    private static void AppendLine(StringBuilder indented, int depth)
    {
        _ = indented.Append(NEW_LINE);
        for (int level = 0; level < depth; level++)
        {
            _ = indented.Append(INDENT);
        }
    }
}

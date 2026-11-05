using IniaAi.AiConnection.Common.ChatConnector.Enums;
using IniaAi.Common.Content;

namespace IniaAi.AiConnection.Common.ChatConnector;

public class ResponseCommonContent
{
    public CommonContentType Type { get; set; }

    public string? TextContent { get; set; }

    public BinaryContent? BinaryContent { get; set; }

    public List<SourceReference>? SourceReferences { get; set; }

    public ResponseCommonContent(CommonContentType type)
    {
        Type = type;
    }

    public ResponseCommonContent(string text)
    {
        TextContent = text;
        Type = CommonContentType.Text;
    }
}

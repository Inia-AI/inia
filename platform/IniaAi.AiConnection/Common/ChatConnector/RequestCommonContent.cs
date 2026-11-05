using IniaAi.AiConnection.Common.ChatConnector.Enums;
using IniaAi.Common.Content;

namespace IniaAi.AiConnection.Common.ChatConnector;

/// <summary>
/// This class is here to provide a common representation of content across
/// different AI providers. Each <see cref="IChatConnector"/> will map its
/// specific content representation from this class to its own.
/// </summary>
public class RequestCommonContent
{
    public CommonContentType Type { get; set; }
    public string? TextContent { get; set; }
    public BinaryContent? BinaryContent { get; set; }
    public string? Title { get; set; }
    public string? Context { get; set; }
    public string? ReferenceId { get; set; }
    public bool IsInputContext { get; set; }
    public bool IsBlockReference { get; set; }

    public RequestCommonContent(string text)
    {
        Type = CommonContentType.Text;
        TextContent = text;
    }

    public RequestCommonContent(BinaryContent binaryContent, CommonContentType type)
    {
        Type = type;
        BinaryContent = binaryContent;
    }
}

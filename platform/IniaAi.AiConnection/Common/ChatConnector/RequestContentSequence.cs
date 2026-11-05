using IniaAi.AiConnection.Common.ChatConnector.Enums;
using IniaAi.Common.Content;
using IniaAi.Common.Enums;
using System.Collections;

namespace IniaAi.AiConnection.Common.ChatConnector;

/// <summary>
/// Represents a sequence of content to be sent to the chat connector.
/// The order of the content is important to the LMM model.
/// </summary>
public class RequestContentSequence : IEnumerable<RequestCommonContent>
{
    private readonly List<RequestCommonContent> _content = [];

    public RequestCommonContent this[int index] => _content[index];

    public void Add(RequestCommonContent item)
    {
        _content.Add(item);
    }

    public void Add(string content)
    {
        _content.Add(new RequestCommonContent(content));
    }

    public void Add(BinaryContent content)
    {
        CommonContentType type = content.MediaType is MediaType.ImagePng or MediaType.ImageJpeg
            ? CommonContentType.Image
            : CommonContentType.File;

        _content.Add(new RequestCommonContent(content, type));
    }

    public IEnumerator<RequestCommonContent> GetEnumerator()
    {
        return _content.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public void Replace(string stringToReplace, string replacement)
    {
        for (int i = 0; i < _content.Count; i++)
        {
            if (_content[i].Type is not CommonContentType.Text)
            {
                continue;
            }

            _content[i].TextContent = _content[i]
                .TextContent?
                .Replace(stringToReplace, replacement);
        }
    }

    public List<RequestCommonContent> GetSourceContents()
    {
        List<RequestCommonContent> sourceContents = [];

        foreach (RequestCommonContent content in this)
        {
            if (!content.IsInputContext && !content.IsBlockReference)
            {
                continue;
            }

            sourceContents.Add(content);
        }

        return sourceContents;
    }

    /// <summary>
    /// Removes any text content items that are null or whitespace only.
    /// </summary>
    /// <param name="contentSequence"></param>
    public void ClearWhiteSpace()
    {
        foreach (RequestCommonContent content in _content
            .Where(i => i.Type is CommonContentType.Text)
            .ToList())
        {
            if (string.IsNullOrWhiteSpace(content.TextContent))
            {
                _ = _content.Remove(content);
            }
        }
    }

    /// <summary>
    /// Condenses the content sequence by merging adjacent text contents into
    /// single content items.
    /// </summary>
    public void Condense()
    {
        if (_content.Count <= 1)
        {
            return;
        }

        List<RequestCommonContent> condensed = [];
        RequestCommonContent? lastTextContent = null;

        foreach (RequestCommonContent content in _content)
        {
            if (content.Type is not CommonContentType.Text
                || content.IsBlockReference
                || content.IsInputContext)
            {
                lastTextContent = null;
                condensed.Add(content);
                continue;
            }

            if (lastTextContent is null)
            {
                lastTextContent = new RequestCommonContent(content.TextContent ?? "");
                condensed.Add(lastTextContent);
            }
            else
            {
                lastTextContent.TextContent += content.TextContent;
            }
        }

        _content.Clear();
        _content.AddRange(condensed);
    }

    public string? GetPlainText()
    {
        string? str;

        if (_content.Count == 0
            || _content.All(c => c.Type is not CommonContentType.Text))
        {
            return null;
        }

        str = "";

        foreach (RequestCommonContent content in this)
        {
            if (content.Type is not CommonContentType.Text)
            {
                str += "[Non-Text Content]";

                continue;
            }

            str += content.TextContent;
        }

        return str.Trim();
    }

    public override string ToString()
    {
        string str = "";

        foreach (RequestCommonContent content in this)
        {
            if (content.Type is CommonContentType.Text)
            {
                str += "StringContent ";
                continue;
            }

            if (content.Type is CommonContentType.Image && content.BinaryContent is not null)
            {
                str += $"{content.BinaryContent.MediaType} ";
            }
        }

        return str;
    }
}

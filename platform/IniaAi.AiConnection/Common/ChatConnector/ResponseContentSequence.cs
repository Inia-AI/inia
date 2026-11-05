using IniaAi.AiConnection.Common.ChatConnector.Enums;
using System.Collections;

namespace IniaAi.AiConnection.Common.ChatConnector;

/// <summary>
/// Represents a sequence of content received from the chat connector.
/// </summary>
public class ResponseContentSequence : IEnumerable<ResponseCommonContent>
{
    public const string STOP_REASON_METADATA_KEY = "StopReason";
    public const string MAX_TOKENS_STOP_REASON = "MaxTokens";

    private readonly List<ResponseCommonContent> _content = [];

    public RequestContentSequence? Request { get; set; }

    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }

    public int TotalTokens => InputTokens + OutputTokens;

    /// <summary>
    /// How many model requests the token counts sum: more than one when the connector re-sent the
    /// request (a thinking-exhausted answer retried at lower effort). A price that depends on one
    /// request's prompt length applies only when this is one.
    /// </summary>
    public int RequestCount { get; set; } = 1;

    /// <summary>
    /// The model wrote no answer, so the sequence carries its thinking in place of one.
    /// </summary>
    public bool IsThinkingOnly { get; set; }

    /// <summary>
    /// The model declined the request. The sequence then holds one error text in place of any
    /// partial answer or thinking, so no caller reads what the model wrote before it stopped as
    /// an answer.
    /// </summary>
    public ModelRefusal? Refusal { get; private set; }

    /// <summary>
    /// Records <paramref name="refusal"/> and replaces the content with its error text. Token
    /// counts and metadata stay: a refused request is still billed.
    /// </summary>
    public void MarkRefused(ModelRefusal refusal)
    {
        Refusal = refusal;
        IsThinkingOnly = false;
        _content.Clear();
        _content.Add(new ResponseCommonContent(refusal.ToAnswerText()));
    }

    /// <summary>
    /// The model id the request was sent to: the connector's own record of it, else the model
    /// the response names. A Bedrock id keeps its inference-profile prefix, which prices it.
    /// </summary>
    public string? EndpointModelId =>
        Metadata.TryGetValue(ModelIdNormalizer.ENDPOINT_MODEL_METADATA_KEY, out object? endpoint) && endpoint is string endpointId
            ? endpointId
            : Metadata.TryGetValue(ModelIdNormalizer.MODEL_METADATA_KEY, out object? model) && model is string modelId
                ? modelId
                : null;

    public bool IsTruncated => Metadata.TryGetValue(STOP_REASON_METADATA_KEY, out object? stopReason)
        && stopReason is string stopReasonText
        && string.Equals(stopReasonText, MAX_TOKENS_STOP_REASON, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Additional metadata specific to the model family.
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = [];

    public ResponseCommonContent this[int index] => _content[index];

    public void Add(ResponseCommonContent item)
    {
        _content.Add(item);
    }

    public IEnumerator<ResponseCommonContent> GetEnumerator()
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

    public string? GetPlainText()
    {
        string? str;

        if (_content.Count == 0
            || _content.All(c => c.Type is not CommonContentType.Text))
        {
            return null;
        }

        str = "";

        foreach (ResponseCommonContent content in this)
        {
            if (content.Type is not CommonContentType.Text)
            {
                continue;
            }

            str += content.TextContent;
        }

        return str.Trim();
    }

    public override string ToString()
    {
        string str = "";

        foreach (ResponseCommonContent content in this)
        {
            if (content.Type is CommonContentType.Text)
            {
                str += "StringContent ";
                continue;
            }

            if (content.Type is CommonContentType.Image)
            {
                str += "ImageContent ";
            }

            if (content.Type is CommonContentType.File)
            {
                str += "FileContent ";
            }
        }

        return str.Trim();
    }
}

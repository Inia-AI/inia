using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.AiConnection.Enums;
using IniaAi.Common.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace IniaAi.AiConnection.Anthropic.ApiModel;

/// <summary>
/// Represents a message.
/// </summary>
internal class Message : JsonSerializable<Message>
{
    /// <summary>
    /// The role of the message sender.
    /// </summary>
    [JsonProperty("role")]
    [JsonConverter(typeof(StringEnumConverter))]
    public MessageSenderRole Role { get; set; } = MessageSenderRole.User;

    /// <summary>
    /// Contents of the message.
    /// </summary>
    [JsonProperty("content")]
    public List<ContentRequest> Content { get; private set; } = [];

    public Message()
    {
    }

    public Message(ContentRequest content)
    {
        Content.Add(content);
    }

    public Message(RequestContentSequence contentSequence)
    {
        Role = MessageSenderRole.User;

        AddToContext(contentSequence);
    }

    private void AddToContext(RequestContentSequence contentSequence)
    {
        foreach (RequestCommonContent commonContent in contentSequence)
        {
            Content.Add(new(commonContent));
        }
    }

    public Message(List<ContentRequest> content)
    {
        Content.AddRange(content);
    }

    public void Add(RequestContentSequence contentSequence)
    {
        AddToContext(contentSequence);
    }
}

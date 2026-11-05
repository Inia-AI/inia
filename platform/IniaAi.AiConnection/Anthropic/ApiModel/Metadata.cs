using IniaAi.Common.Serialization;
using Newtonsoft.Json;

namespace IniaAi.AiConnection.Anthropic.ApiModel;

/// <summary>
/// An object describing metadata about the request.
/// </summary>
internal class Metadata : JsonSerializable<Metadata>
{
    /// <summary>
    /// An external identifier for the user who is associated with the request.
    /// This should be a uuid, hash value, or other opaque identifier.
    /// Anthropic may use this id to help detect abuse. Do not include any
    /// identifying information such as name, email address, or phone number.
    /// </summary>
    [JsonProperty("user_id")]
    public Guid UserId { get; set; } = Guid.NewGuid();
}

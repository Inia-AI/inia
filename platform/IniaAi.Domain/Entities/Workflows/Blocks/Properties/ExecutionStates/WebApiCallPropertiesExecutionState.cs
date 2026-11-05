using IniaAi.Common.Reflection;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates.Abstractions;
using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;

/// <summary>
/// Execution state for WebApiCallProperties. Holds resolved Url and RequestPayload.
/// </summary>
public class WebApiCallPropertiesExecutionState : BlockPropertiesExecutionStateBase<WebApiCallPropertiesExecutionState>
{
    [SanitizePlaceholderValues(PlaceholderSanitization.Url)]
    [JsonProperty(nameof(Url), NullValueHandling = NullValueHandling.Ignore)]
    public string? Url { get; set; }

    [SanitizePlaceholderValues(PlaceholderSanitization.Json)]
    [JsonProperty(nameof(RequestPayload), NullValueHandling = NullValueHandling.Ignore)]
    public string? RequestPayload { get; set; }

    [JsonProperty(nameof(HttpMethod), NullValueHandling = NullValueHandling.Ignore)]
    public string? HttpMethod { get; set; }

    [SanitizePlaceholderValues(PlaceholderSanitization.HeaderValue)]
    [JsonProperty(nameof(ContentType), NullValueHandling = NullValueHandling.Ignore)]
    public string? ContentType { get; set; }

    [SanitizePlaceholderValues(PlaceholderSanitization.HeaderValue)]
    [JsonProperty(nameof(AcceptHeader), NullValueHandling = NullValueHandling.Ignore)]
    public string? AcceptHeader { get; set; }

    [SanitizePlaceholderValues(PlaceholderSanitization.Url)]
    [JsonProperty(nameof(PollCompletionUrl), NullValueHandling = NullValueHandling.Ignore)]
    public string? PollCompletionUrl { get; set; }

    [SanitizePlaceholderValues(PlaceholderSanitization.HeaderValue)]
    public Dictionary<string, string> AdditionalHeaders { get; set; } = [];

    [JsonConstructor]
    [System.Text.Json.Serialization.JsonConstructor]
    internal WebApiCallPropertiesExecutionState()
    {
    }

    public WebApiCallPropertiesExecutionState(WebApiCallProperties source)
    {
        Url = source.UrlTemplate;
        RequestPayload = source.RequestPayloadTemplate;
        HttpMethod = source.HttpMethod;
        ContentType = source.ContentType;
        AcceptHeader = source.AcceptHeader;
        PollCompletionUrl = source.PollCompletionUrlTemplate;
        AdditionalHeaders = new Dictionary<string, string>(source.AdditionalHeaders);
    }
}

using IniaAi.Common.Reflection;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties;

public class WebApiCallProperties : BlockProperties
{
    public const string URL_TEMPLATE_LABEL = "URL Template";
    public const string COMPLETION_URL_TEMPLATE_LABEL = "On Completion: Fetch URL Template";

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the request URL. It can contain placeholders that get
    /// replaced at execution time. Use
    /// <see cref="WebApiCallPropertiesExecutionState.Url"/> for the actual
    /// URL after replacements.
    /// </summary>
    [JsonProperty(nameof(UrlTemplate))]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? UrlTemplate { get; set; }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the request payload. It can contain placeholders that
    /// get replaced at execution time. Use
    /// <see cref="WebApiCallPropertiesExecutionState.RequestPayload"/> for
    /// the actual request payload after replacements.
    /// </summary>
    [JsonProperty(nameof(RequestPayloadTemplate), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? RequestPayloadTemplate { get; set; }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the HTTP method. It can contain placeholders that get
    /// replaced at execution time. Use
    /// <see cref="WebApiCallPropertiesExecutionState.HttpMethod"/> for the
    /// actual HTTP method after replacements.
    /// </summary>
    [JsonProperty(nameof(HttpMethod))]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string HttpMethod { get; set; } = "GET";

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the Content-Type header. It can contain placeholders
    /// that get replaced at execution time. Use
    /// <see cref="WebApiCallPropertiesExecutionState.ContentType"/> for the
    /// actual content type after replacements.
    /// </summary>
    [JsonProperty(nameof(ContentType))]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string ContentType { get; set; } = "application/json";

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the Accept header. It can contain placeholders that get
    /// replaced at execution time. Use
    /// <see cref="WebApiCallPropertiesExecutionState.AcceptHeader"/> for
    /// the actual accept header after replacements.
    /// </summary>
    [JsonProperty(nameof(AcceptHeader))]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string AcceptHeader { get; set; } = "application/json";

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the additional HTTP headers. Values can contain
    /// placeholders that get replaced at execution time. Use
    /// <see cref="WebApiCallPropertiesExecutionState.AdditionalHeaders"/>
    /// for the actual additional headers after replacements.
    /// </summary>
    [JsonProperty(nameof(AdditionalHeaders))]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Dictionary<string, string> AdditionalHeaders { get; set; } = [];

    public bool ShouldSerializeAdditionalHeaders()
    {
        return AdditionalHeaders.Count > 0;
    }

    /// <summary>
    /// Delay in seconds after request completion (for async API patterns).
    /// </summary>
    [JsonProperty(nameof(DelayAfterRequestSeconds), NullValueHandling = NullValueHandling.Ignore)]
    public int? DelayAfterRequestSeconds { get; set; }

    /// <summary>
    /// Dot-path to the status field in the JSON response (e.g. <c>status</c> or
    /// <c>run.status</c>). A non-empty value enables poll-until-condition mode:
    /// the URL is called repeatedly until the status reaches a success value, a
    /// failure value, or the timeout. When empty, the block behaves as a single
    /// request. Polling calls the resolved (post-placeholder) URL and evaluates this
    /// path against the JSON response body; the path itself is matched verbatim and
    /// is not placeholder-resolved.
    /// </summary>
    [JsonProperty(nameof(PollUntilJsonPath), NullValueHandling = NullValueHandling.Ignore)]
    public string? PollUntilJsonPath { get; set; }

    /// <summary>
    /// Comma-separated status values meaning "done" (e.g. <c>completed,succeeded</c>).
    /// Matched case-insensitively. Required when polling is enabled.
    /// </summary>
    [JsonProperty(nameof(PollUntilSuccessValues), NullValueHandling = NullValueHandling.Ignore)]
    public string? PollUntilSuccessValues { get; set; }

    /// <summary>
    /// Optional comma-separated status values meaning "failed" (e.g.
    /// <c>failed,error,cancelled</c>). Matched case-insensitively. When the status
    /// reaches one of these the block fails fast instead of waiting out the timeout.
    /// </summary>
    [JsonProperty(nameof(PollUntilFailureValues), NullValueHandling = NullValueHandling.Ignore)]
    public string? PollUntilFailureValues { get; set; }

    /// <summary>
    /// Seconds between polls. Defaults to 15 and is clamped to a sensible minimum
    /// so the API is not hammered. Only used when polling is enabled.
    /// </summary>
    [JsonProperty(nameof(PollIntervalSeconds), NullValueHandling = NullValueHandling.Ignore)]
    public int? PollIntervalSeconds { get; set; }

    /// <summary>
    /// Maximum total polling time in seconds. Defaults to 600 and is clamped to a
    /// ceiling to prevent runaways. Reaching it throws a timeout. Only used when
    /// polling is enabled.
    /// </summary>
    [JsonProperty(nameof(PollTimeoutSeconds), NullValueHandling = NullValueHandling.Ignore)]
    public int? PollTimeoutSeconds { get; set; }

    /// <summary>
    /// Optional URL template fetched once (GET, same headers) after polling reaches a
    /// success value; its response becomes the block output instead of the final status
    /// JSON. Collapses the poll-status + fetch-result block pair into one block. It can
    /// contain placeholders that get replaced at execution time; use
    /// <see cref="WebApiCallPropertiesExecutionState.PollCompletionUrl"/> for the actual
    /// URL after replacements. Only valid when polling is enabled.
    /// </summary>
    [JsonProperty(nameof(PollCompletionUrlTemplate), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? PollCompletionUrlTemplate { get; set; }

    [NotMapped]
    [JsonProperty(nameof(ExecutionState), NullValueHandling = NullValueHandling.Ignore)]
    public WebApiCallPropertiesExecutionState? ExecutionState { get; set; }

    public WebApiCallProperties()
    {
        Type = BlockTypes.WebApiCall;
    }

    public override BlockProperties Clone()
    {
        var clone = new WebApiCallProperties();
        CloneHelper.CopyScalars(this, clone);
        return clone;
    }

    public override void SetExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState = new WebApiCallPropertiesExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void UpdateExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState ??= new WebApiCallPropertiesExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void ClearExecutionState()
    {
        ExecutionState = null;
    }

    /// <summary>
    /// What is wrong, in words for the block's author, with each address of the block whose host a
    /// placeholder could choose: a value can come from a model's answer about a stranger's document.
    /// </summary>
    public IEnumerable<string> FixedHostProblems(string blockName)
    {
        return AddressTemplates()
            .Select(address => FixedHostProblem(blockName, address.Label, address.Template))
            .OfType<string>();
    }

    /// <summary>The block's address templates, each with its property name and the label its field shows.</summary>
    public IEnumerable<(string Field, string Label, string? Template)> AddressTemplates()
    {
        yield return (nameof(UrlTemplate), URL_TEMPLATE_LABEL, UrlTemplate);
        yield return (nameof(PollCompletionUrlTemplate), COMPLETION_URL_TEMPLATE_LABEL, PollCompletionUrlTemplate);
    }

    public static string? FixedHostProblem(string blockName, string label, string? template)
    {
        if (string.IsNullOrWhiteSpace(template) || WebAddressOrigin.Of(template) is not null)
        {
            return null;
        }

        return $"The {label} of block '{blockName}' must name its host directly, such as https://api.example.com/, "
            + "because a placeholder cannot choose where the block sends its request. "
            + "Write the host into the field, and put placeholders only after the first /, ? or #.";
    }
}

using IniaAi.Common;
using IniaAi.Common.Enums;
using IniaAi.Common.Extensions;
using Newtonsoft.Json;

namespace IniaAi.AiConnection.Anthropic.ApiModel;

/// <summary>
/// The <c>output_config</c> request object. Only adaptive-generation models
/// accept it; <see cref="ModelRequestShaper"/> writes it, and the low-effort
/// resend of <see cref="ThinkingExhaustion"/> rewrites it for those models only,
/// so the field never reaches a model that would reject it.
/// </summary>
internal class OutputConfig
{
    [JsonProperty("effort", NullValueHandling = NullValueHandling.Ignore)]
    public string? Effort { get; set; }

    public ModelEffort? ToEffort()
    {
        return Effort is not null && Utils.TryParseEnumFromEnumMemberAttribute(Effort, out ModelEffort effort)
            ? effort
            : null;
    }

    public static OutputConfig? FromEffort(ModelEffort? effort)
    {
        return effort is null
            ? null
            : new OutputConfig { Effort = effort.Value.GetEnumMember() };
    }
}

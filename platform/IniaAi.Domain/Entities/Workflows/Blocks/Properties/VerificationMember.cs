using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties;

/// <summary>
/// One verified member of a <see cref="VerificationProperties"/> group: the replacement tag of
/// an Agent block, and the redo mode the loop uses when the verifier fails that member. The
/// mode lives on the member entry — never in a parallel map — so a tag absent from the member
/// list can hold no orphaned setting. Deliberately a plain class with a parameterless
/// constructor and an explicit initializer: a positional record would fill a missing member
/// through the constructor with <c>default</c> and silently flip the persisted default, and
/// "an absent field reads back as Revise" is what makes the default true for workflows saved
/// before the field existed.
/// </summary>
public class VerificationMember
{
    [JsonProperty(nameof(MemberTag))]
    public string MemberTag { get; set; } = string.Empty;

    [JsonProperty(nameof(RedoMode))]
    [JsonConverter(typeof(StringEnumConverter))]
    public VerificationRedoMode RedoMode { get; set; } = VerificationRedoMode.Revise;

    /// <summary>
    /// Re-points the entry at a different Agent block and resets the redo mode to the Revise
    /// default: the mode was chosen for the previous member's work, and carrying it over would
    /// silently apply a judgment nobody made about the new member.
    /// </summary>
    public void PointAt(string? tag)
    {
        string newTag = tag ?? string.Empty;
        if (string.Equals(MemberTag, newTag, StringComparison.Ordinal))
        {
            return;
        }

        MemberTag = newTag;
        RedoMode = VerificationRedoMode.Revise;
    }

    public VerificationMember Clone()
    {
        return new VerificationMember
        {
            MemberTag = MemberTag,
            RedoMode = RedoMode,
        };
    }
}

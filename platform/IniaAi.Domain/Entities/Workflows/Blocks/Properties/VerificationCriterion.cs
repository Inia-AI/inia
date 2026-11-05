using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties;

/// <summary>
/// One declared criterion of a verification block. The ID is load-bearing and stable across
/// saves: the no-progress exit compares failed-ID sets, the failure record keys rows by it,
/// and the verdict envelope's <c>criterionId</c> enum is sourced from it — renaming a
/// criterion is a new ID. The label is what release markers and the failure record show a
/// human. Applicability names the members the criterion judges; an empty list means group
/// scope — the criterion compares members to each other and belongs to no single one.
/// </summary>
public class VerificationCriterion
{
    /// <summary>
    /// The failure record stores the ID in a bounded column, so a longer ID would fail the
    /// record insert after the whole group has been paid for. Save-time validation holds it
    /// here instead, pre-spend.
    /// </summary>
    public const int ID_MAX_LENGTH = 450;

    [JsonProperty(nameof(Id))]
    public string Id { get; set; } = string.Empty;

    [JsonProperty(nameof(Label))]
    public string Label { get; set; } = string.Empty;

    [JsonProperty(
        nameof(AppliesTo),
        NullValueHandling = NullValueHandling.Ignore,
        ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<string> AppliesTo
    {
        get;
        set => field = value ?? [];
    } = [];

    public VerificationCriterion Clone()
    {
        return new VerificationCriterion
        {
            Id = Id,
            Label = Label,
            AppliesTo = [.. AppliesTo],
        };
    }
}

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace IniaAi.Domain.Proposals;

/// <summary>
/// The changes an agent proposed to a workflow, paired field by field against the workflow it
/// was given (the base). Each change carries the value its field had in the base and the value
/// proposed, so applying it can tell whether someone changed the same field in the meantime.
/// </summary>
public sealed class WorkflowProposal
{
    private const string SECTIONS_LIST = "sections";

    public List<ProposedChange> Changes { get; init; } = [];

    /// <summary>What the agent said it meant to change, from its summary file.</summary>
    public List<ProposalSummaryEntry> Summary { get; init; } = [];

    /// <summary>The agent's account of the whole proposal in plain words; null when it wrote none.</summary>
    public string? Overview { get; set; }

    /// <summary>The save checks the proposed workflow still fails after the last round.</summary>
    public List<ProposalCheckError> CheckErrors { get; init; } = [];

    /// <summary>
    /// What a save would ask the user to confirm rather than refuse, such as agent template tags
    /// nothing supplies. They refuse no change.
    /// </summary>
    public List<ProposalCheckError> Warnings { get; init; } = [];

    /// <summary>How many rounds the agent took, the first answer included.</summary>
    public int Rounds { get; set; } = 1;

    public string ToJson()
    {
        return JsonConvert.SerializeObject(this, ProposalJson.ModelSettings);
    }

    public static WorkflowProposal FromJson(string json)
    {
        return JsonConvert.DeserializeObject<WorkflowProposal>(json, ProposalJson.ModelSettings)
            ?? throw new JsonSerializationException("The proposal is empty");
    }

    /// <summary>
    /// The <see cref="ProposedChange.Incidental"/> renumbering of every list the chosen changes add
    /// to, take from or move within, which applies with them so an added block lands where the
    /// agent put it. The chosen changes themselves are not returned.
    /// </summary>
    public List<ProposedChange> RenumberingFor(IReadOnlyCollection<ProposedChange> chosen)
    {
        var lists = new HashSet<string>(chosen.SelectMany(ListsTouched), StringComparer.Ordinal);
        var ids = new HashSet<string>(chosen.Select(static change => change.Id), StringComparer.Ordinal);

        return [.. Changes.Where(change => change.Incidental
            && !ids.Contains(change.Id)
            && ListOf(change) is string list
            && lists.Contains(list))];
    }

    /// <summary>The ordered list an <c>Order</c> edit renumbers: the sections, or one section's blocks.</summary>
    public static string? ListOf(ProposedChange change)
    {
        return change.Scope switch
        {
            ProposedChangeScope.Section => SECTIONS_LIST,
            ProposedChangeScope.Block => change.SectionKey,
            _ => null,
        };
    }

    private static IEnumerable<string> ListsTouched(ProposedChange change)
    {
        switch (change.Scope)
        {
            case ProposedChangeScope.Section when change.Kind != ProposedChangeKind.FieldEdited || change.Field == ProposalJson.ORDER:
                yield return SECTIONS_LIST;
                break;
            case ProposedChangeScope.Block when change.SectionKey is string section:
                yield return section;
                if (change.Kind == ProposedChangeKind.Moved && ProposalJson.StringOf(ProposalJson.Parse(change.BaseJson!)) is string from)
                {
                    yield return from;
                }

                break;
        }
    }
}

/// <summary>
/// One field of one node, or one node added, removed or moved. Values are JSON text: a string
/// field's value is a JSON string. A null value means the field or node is absent on that side.
/// </summary>
public sealed record ProposedChange
{
    /// <summary>Unique within its proposal and stable for the same edit.</summary>
    public string Id { get; init; } = string.Empty;

    [JsonConverter(typeof(StringEnumConverter))]
    public ProposedChangeScope Scope { get; init; }

    [JsonConverter(typeof(StringEnumConverter))]
    public ProposedChangeKind Kind { get; init; }

    /// <summary>
    /// The node: a StableBlockId, a StableSectionId, a variable's tag, a document class key, a
    /// signature or regression field name; null for a workflow-level field. A node the agent added
    /// keeps the key it gave it, or a <c>new-</c> key; applying it mints the real stable id.
    /// </summary>
    public string? TargetKey { get; init; }

    /// <summary>For a block, the section it sits in once the change is applied.</summary>
    public string? SectionKey { get; init; }

    /// <summary>For a field edit, the field: <c>Name</c>, <c>Properties.UserPromptTemplate</c>.</summary>
    public string? Field { get; init; }

    public string? BaseJson { get; init; }

    public string? NewJson { get; init; }

    public string? Name { get; init; }

    public string? Tag { get; init; }

    public string? BlockType { get; init; }

    /// <summary>
    /// The change adds or alters what a block sends to an outside address: a web call's address,
    /// headers, method or payload, or a search query. It is never accepted by an accept-all.
    /// </summary>
    public bool SendsDataOutside { get; set; }

    /// <summary>The node follows a Library component; this workflow's copy will differ from it.</summary>
    public bool LibraryLinked { get; init; }

    /// <summary>The agent changed this block without listing it in its summary.</summary>
    public bool MissingFromSummary { get; init; }

    /// <summary>
    /// An <c>Order</c> edit that only renumbers a list around an added, removed or moved item; the
    /// list's own order is unchanged. Nobody reviews it: it applies with the change that caused it.
    /// </summary>
    public bool Incidental { get; set; }

    /// <summary>What stops the change being accepted; null when nothing does.</summary>
    public ProposalRefusal? Refusal { get; set; }

    /// <summary>The refusal in the user's words, filled in by whoever presents the proposal.</summary>
    public string? RefusedReason { get; set; }

    [JsonIgnore]
    public bool CanAccept => Refusal is null && RefusedReason is null;
}

public enum ProposalRefusal
{
    LibraryLink = 0,
    FailedCheck = 1,
    ReportText = 2,
}

public enum ProposedChangeScope
{
    Block = 0,
    Section = 1,
    Variable = 2,
    DocumentClass = 3,
    SignatureField = 4,
    RegressionField = 5,
    Workflow = 6,
}

public enum ProposedChangeKind
{
    FieldEdited = 0,
    Added = 1,
    Removed = 2,
    Moved = 3,
}

/// <param name="StableBlockId">The block the entry is about, when it is about a block.</param>
/// <param name="Section">The section the entry is about, by StableSectionId or title.</param>
/// <param name="What">One plain sentence.</param>
/// <param name="FeedbackIds">The report feedback items the change fixes, when it fixes any.</param>
public sealed record ProposalSummaryEntry(
    string? StableBlockId,
    string? Section,
    string What,
    IReadOnlyList<string>? FeedbackIds = null);

/// <param name="Check">Which check failed (see <see cref="ProposalChecks"/>).</param>
/// <param name="BlockKey">The block the error was traced to, when it could be.</param>
public sealed record ProposalCheckError(string Check, string Message, string? BlockKey = null);

/// <summary>The names of the checks a proposed workflow runs, as they appear on an error.</summary>
public static class ProposalChecks
{
    public const string FILES = "Files";
    public const string SCHEMA = "Schema";
    public const string STRUCTURE = "Structure";
    public const string LIBRARY = "Library";
    public const string AGENT_TAGS = "AgentTags";
    public const string AGENT_GATES = "AgentGates";
    public const string SIGNATURE_FIELDS = "SignatureFields";
    public const string REPORT_TEXT = "ReportText";
    public const string APPLY = "Apply";
}

using ErrorOr;
using IniaAi.Application.Workflows.EditorAgent;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Proposals;

namespace IniaAi.WebApp.Components.WorkflowEditor.Agent;

public enum EditorAgentReviewVerb
{
    Accept,
    Reject,
    Undo,
}

/// <param name="ChangeIds">The changes acted on; null for every change the verb covers on its own.</param>
/// <param name="TakeProposed">Changes whose conflict the user settled for the agent's value.</param>
/// <param name="ConfirmOutbound">Changes that send data outside which the user accepted on a card warning about it.</param>
public sealed record EditorAgentReviewAction(
    string TurnId,
    EditorAgentReviewVerb Verb,
    IReadOnlyList<string>? ChangeIds = null,
    IReadOnlyCollection<string>? TakeProposed = null,
    IReadOnlyCollection<string>? ConfirmOutbound = null);

/// <param name="Error">Shown to the user when the action was refused.</param>
/// <param name="Edit">What changed on the editor's workflow, when anything did.</param>
public sealed record EditorAgentReviewResult(string? Error, EditorAgentWorkflowEdit? Edit);

/// <summary>
/// The user's review of one proposal: the decisions go to the agent service, the conflicts and
/// notes stay here between renders, and the card reads them.
/// </summary>
public sealed class EditorAgentProposalReview
{
    private readonly IWorkflowEditorAgentService _service;
    private readonly string _organizationId;
    private readonly string _userId;
    private readonly Dictionary<string, ProposalConflict> _conflicts = new(StringComparer.Ordinal);
    private readonly List<string> _notes = [];

    public EditorAgentProposalReview(
        IWorkflowEditorAgentService service,
        string organizationId,
        string userId,
        string turnId,
        EditorAgentProposalView proposal)
    {
        _service = service;
        _organizationId = organizationId;
        _userId = userId;
        TurnId = turnId;
        Proposal = proposal;
    }

    public string TurnId { get; }

    public EditorAgentProposalView Proposal { get; set; }

    public IReadOnlyDictionary<string, ProposalConflict> Conflicts => _conflicts;

    /// <summary>What the last action left to say: changes not applied and why, or what Accept all left out.</summary>
    public IReadOnlyList<string> Notes => _notes;

    /// <summary>How many changes were applied by the last accept, for the card's quiet confirmation.</summary>
    public int LastApplied { get; private set; }

    public bool IsBusy { get; private set; }

    public static bool IsUndecided(EditorAgentChangeView view)
    {
        return view.Decision is null or EditorAgentChangeDecisionStatus.Undone;
    }

    /// <summary>
    /// A change Accept all leaves out: it sends data to an outside address, the agent did not list
    /// it, or it follows the Library. It is accepted on its own card.
    /// </summary>
    public static bool NeedsOwnAccept(ProposedChange change)
    {
        return change.SendsDataOutside || change.MissingFromSummary || change.LibraryLinked;
    }

    public IEnumerable<EditorAgentChangeView> Undecided => Proposal.Changes.Where(static view => IsUndecided(view) && !view.Change.Incidental);

    public IEnumerable<EditorAgentChangeView> AcceptAllCovers => Undecided
        .Where(static view => view.Change.CanAccept && !NeedsOwnAccept(view.Change));

    public string? AcceptAllSkipText()
    {
        int own = Undecided.Count(static view => view.Change.CanAccept && NeedsOwnAccept(view.Change));
        int refused = Undecided.Count(static view => !view.Change.CanAccept);
        var parts = new List<string>();
        if (own > 0)
        {
            parts.Add(own == 1 ? "1 change that needs its own review" : $"{own} changes that need their own review");
        }

        if (refused > 0)
        {
            parts.Add(refused == 1 ? "1 change that cannot be accepted" : $"{refused} changes that cannot be accepted");
        }

        return parts.Count == 0 ? null : $"Accept all leaves out {string.Join(" and ", parts)}.";
    }

    public async Task<EditorAgentReviewResult> RunAsync(EditorAgentReviewAction action, Workflow workflow)
    {
        IsBusy = true;
        try
        {
            return action.Verb switch
            {
                EditorAgentReviewVerb.Accept => await AcceptAsync(action, workflow),
                EditorAgentReviewVerb.Reject => await RejectAsync(action.ChangeIds),
                _ => await UndoAsync(workflow, action.ChangeIds),
            };
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<EditorAgentReviewResult> AcceptAsync(EditorAgentReviewAction action, Workflow workflow)
    {
        string? skipped = action.ChangeIds is null ? AcceptAllSkipText() : null;
        IReadOnlyList<string>? changeIds = action.ChangeIds ?? [.. AcceptAllCovers.Select(static view => view.Change.Id)];
        _notes.Clear();

        ErrorOr<EditorAgentApplyResult> accepted = await _service.AcceptChangesAsync(
            new EditorAgentAcceptRequest(_organizationId, _userId, TurnId, changeIds, workflow)
            {
                TakeProposed = action.TakeProposed ?? [],
                ConfirmOutbound = action.ConfirmOutbound ?? [],
            });
        if (accepted.IsError)
        {
            return new EditorAgentReviewResult(accepted.FirstError.Description, null);
        }

        EditorAgentApplyResult result = accepted.Value;
        Dictionary<string, ProposedChange> byId = Proposal.Changes
            .ToDictionary(static view => view.Change.Id, static view => view.Change, StringComparer.Ordinal);

        foreach (ProposalAppliedChange applied in result.Applied)
        {
            _ = _conflicts.Remove(applied.ChangeId);
        }

        foreach (ProposalConflict conflict in result.Conflicts)
        {
            _conflicts[conflict.ChangeId] = conflict;
        }

        foreach (ProposalNotApplied notApplied in result.NotApplied.Where(item => !_conflicts.ContainsKey(item.ChangeId)))
        {
            string subject = byId.TryGetValue(notApplied.ChangeId, out ProposedChange? change)
                ? EditorAgentChangeText.Subject(change, Proposal, workflow)
                : EditorAgentChangeText.UNKNOWN_CHANGE;
            _notes.Add($"Not applied, {subject}: {notApplied.Reason}");
        }

        _notes.AddRange(result.Warnings);

        if (skipped is not null)
        {
            _notes.Add(skipped);
        }

        LastApplied = result.Applied.Count;

        EditorAgentWorkflowEdit? edit = null;
        if (result.Applied.Count > 0)
        {
            List<string> touched = [.. result.Applied
                .Select(applied => byId.TryGetValue(applied.ChangeId, out ProposedChange? change)
                    ? EditorAgentChangeText.ChangedStableId(change, applied.MintedStableId)
                    : null)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)];
            edit = new EditorAgentWorkflowEdit(Proposal.Id, touched, Undone: false);
        }

        return new EditorAgentReviewResult(null, edit);
    }

    private async Task<EditorAgentReviewResult> RejectAsync(IReadOnlyList<string>? changeIds)
    {
        ErrorOr<Success> rejected = await _service.RejectChangesAsync(_organizationId, _userId, TurnId, changeIds);
        if (rejected.IsError)
        {
            return new EditorAgentReviewResult(rejected.FirstError.Description, null);
        }

        if (changeIds is null)
        {
            _conflicts.Clear();
        }
        else
        {
            foreach (string changeId in changeIds)
            {
                _ = _conflicts.Remove(changeId);
            }
        }

        return new EditorAgentReviewResult(null, null);
    }

    /// <param name="changeIds">The changes to take back, such as one section's; null for every accepted change.</param>
    private async Task<EditorAgentReviewResult> UndoAsync(Workflow workflow, IReadOnlyList<string>? changeIds)
    {
        _notes.Clear();
        ErrorOr<EditorAgentUndoResult> undone = await _service.UndoChangesAsync(_organizationId, _userId, TurnId, workflow, changeIds);
        if (undone.IsError)
        {
            return new EditorAgentReviewResult(undone.FirstError.Description, null);
        }

        _conflicts.Clear();
        LastApplied = 0;
        int kept = undone.Value.Conflicts.Count;
        if (kept > 0)
        {
            _notes.Add(kept == 1
                ? "1 change stays as it is, because you edited it after accepting it."
                : $"{kept} changes stay as they are, because you edited them after accepting them.");
        }

        List<string> unmarked = changeIds is null
            ? []
            : [.. Proposal.Changes
                .Where(view => undone.Value.UndoneChangeIds.Contains(view.Change.Id))
                .Select(view => EditorAgentChangeText.ChangedStableId(view.Change, view.MintedStableId))
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)];
        return new EditorAgentReviewResult(null, new EditorAgentWorkflowEdit(Proposal.Id, unmarked, Undone: true));
    }
}

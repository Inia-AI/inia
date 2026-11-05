using ErrorOr;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.ReportFeedback;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Proposals;

namespace IniaAi.Application.Workflows.EditorAgent;

/// <summary>
/// The agent beside the workflow editor, as the Blazor editor calls it in-process. Every method
/// is scoped to one user in one organization: a conversation belongs to the user who started it,
/// and another user's conversation reads as not found. The caps, the order of turns, cancel and
/// retention belong to the shared conversation layer (<see cref="IAgentConversationTurns"/>);
/// this service supplies the editor's checks and the draft. Turns run as jobs on the host's job
/// queue, never on the caller's connection; state lives in the database, so a page that reloads
/// calls <see cref="GetOrStartConversationAsync"/> again and finds the running and waiting turns.
/// </summary>
public interface IWorkflowEditorAgentService
{
    /// <summary>
    /// True when the deployment turned the editor agent on and the agent runtime is enabled. When
    /// false the panel stays hidden and every other method answers <see cref="EditorAgentErrors.Disabled"/>.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// The models a user may pick: the platform's advanced model (the default) and its basic
    /// model as the organization's settings resolve them, each with its price per million tokens
    /// and what it is good at, plus <paramref name="currentModelId"/> when the conversation is on
    /// another model the runtime still allows. A default the runtime does not allow is left out;
    /// when it allows neither, every allowed model is offered. Exactly one is marked default.
    /// </summary>
    Task<IReadOnlyList<EditorAgentModelOption>> ListModelsAsync(
        string organizationId,
        string? currentModelId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The user's current conversation about this workflow type, or a new one on the default model
    /// when none is live. Refused with <see cref="EditorAgentErrors.WorkflowTypeNotFound"/> for a
    /// type that does not exist and <see cref="EditorAgentErrors.Forbidden"/> when the caller holds
    /// no editor role or their organization has no access to the type (a super admin needs no
    /// access row).
    /// </summary>
    Task<ErrorOr<EditorAgentConversationView>> GetOrStartConversationAsync(
        string organizationId,
        string userId,
        string workflowTypeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a fresh conversation (the New conversation button) and ends the previous live one:
    /// its waiting messages are cancelled, a running one finishes, and the next retention sweep
    /// deletes it. A null model takes the default. Checked like
    /// <see cref="GetOrStartConversationAsync"/>.
    /// </summary>
    Task<ErrorOr<EditorAgentConversationView>> StartNewConversationAsync(
        string organizationId,
        string userId,
        string workflowTypeId,
        string? modelId,
        CancellationToken cancellationToken = default);

    Task<ErrorOr<EditorAgentConversationView>> GetConversationAsync(
        string organizationId,
        string userId,
        string conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of the caller's organization's past reports (runs), newest first, for the report
    /// picker: never another organization's. Without a query, the latest reports of the open
    /// workflow's type. <see cref="EditorAgentReportQuery.WorkflowTypeId"/> may name another
    /// type from <see cref="ListReportWorkflowTypesAsync"/>; any other type is refused with
    /// <see cref="EditorAgentErrors.Forbidden"/>. Checked like <see cref="GetOrStartConversationAsync"/>.
    /// </summary>
    Task<ErrorOr<EditorAgentReportPage>> ListReportsAsync(
        string organizationId,
        string userId,
        string workflowTypeId,
        EditorAgentReportQuery? query = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The workflow types whose reports the user may pick as examples while editing
    /// <paramref name="workflowTypeId"/>: that type first, then every type their organization has
    /// access to. Checked like <see cref="GetOrStartConversationAsync"/>.
    /// </summary>
    Task<ErrorOr<IReadOnlyList<EditorAgentWorkflowTypeOption>>> ListReportWorkflowTypesAsync(
        string organizationId,
        string userId,
        string workflowTypeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Snapshots the editor's workflow as the turn's draft and records the turn. Returns as soon as
    /// the turn is recorded; progress arrives through <see cref="IEditorAgentNotifications"/>.
    /// <para>
    /// Sent while the conversation is idle, the turn starts at once (Queued, then Running). Sent
    /// while an earlier message is still in flight, it is kept as a waiting turn (status Queued,
    /// <see cref="EditorAgentTurnView.IsWaiting"/> true) and starts on its own when the messages
    /// before it have settled, in the order they were sent, also across a reload or a restart.
    /// Cancelling a waiting turn takes it out of the line.
    /// </para>
    /// Refused with <see cref="AgentConversationErrors.TooManyRunning"/> only when the turn would start
    /// now and the user already has the cap of messages running (waiting ones do not count); with
    /// <see cref="EditorAgentErrors.WorkflowMismatch"/> when the workflow is not of the
    /// conversation's type; with <see cref="EditorAgentErrors.DraftTooLarge"/> above the draft size
    /// cap; with <see cref="EditorAgentErrors.DailySpendUsed"/> once the organization's spend today
    /// reaches its daily batches (<see cref="UnlockDailySpendAsync"/> adds one); and with
    /// <see cref="EditorAgentErrors.Forbidden"/> without an editor role. The reports
    /// in <see cref="EditorAgentMessage.ReportIds"/> are staged for this turn only; more than
    /// <see cref="EditorAgentReports.MAX_PICKED"/> are refused with
    /// <see cref="EditorAgentErrors.TooManyReports"/>, and one that is not a report in the caller's
    /// organization of a type from <see cref="ListReportWorkflowTypesAsync"/> with
    /// <see cref="EditorAgentErrors.ReportNotFound"/>. With
    /// <see cref="EditorAgentMessage.FeedbackItemIds"/> the turn is also sent those feedback items,
    /// of the ones <see cref="ListOpenFeedbackAsync"/> lists, and the runs holding the most of them
    /// fill the report slots the user's picks leave. More than <see cref="EditorAgentFeedback.MAX_SENT"/>
    /// are refused with <see cref="EditorAgentErrors.TooManyFeedback"/>; an item closed since is left
    /// out, and with none still open the message is refused with <see cref="EditorAgentErrors.NoOpenFeedback"/>.
    /// </summary>
    Task<ErrorOr<EditorAgentTurnView>> SendMessageAsync(
        EditorAgentMessage message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// How many open feedback items the reports of the workflow type hold, for the "Use feedback"
    /// button: those of the caller's organization, or of every organization for a super admin. An
    /// item on a deleted report does not count. Checked like <see cref="GetOrStartConversationAsync"/>.
    /// </summary>
    Task<ErrorOr<int>> CountOpenFeedbackAsync(
        string organizationId,
        string userId,
        string workflowTypeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The open feedback items <see cref="CountOpenFeedbackAsync"/> counts, newest first, at most
    /// <see cref="EditorAgentFeedback.MAX_LISTED"/>, for the user to choose which the agent gets.
    /// </summary>
    Task<ErrorOr<IReadOnlyList<EditorAgentFeedbackOption>>> ListOpenFeedbackAsync(
        string organizationId,
        string userId,
        string workflowTypeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a feedback item for good, whatever its status, from the "Choose feedback" list.
    /// The item must be one the caller may read there: on a report of the workflow type, of the
    /// caller's organization, or of any organization for a super admin. Refused with
    /// <see cref="EditorAgentErrors.FeedbackNotFound"/> for any other item. Checked like
    /// <see cref="GetOrStartConversationAsync"/>. A message already sent the item keeps its copy,
    /// and a later save that names it skips it.
    /// </summary>
    Task<ErrorOr<Deleted>> DeleteFeedbackAsync(
        string organizationId,
        string userId,
        string workflowTypeId,
        string feedbackItemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Dismisses a feedback item with the reason the agent gave when it suggested dismissing it in
    /// the turn. Refused with <see cref="EditorAgentErrors.FeedbackNotSuggested"/> when the turn
    /// made no such suggestion or is not the caller's, and with
    /// <see cref="EditorAgentErrors.Forbidden"/> when the caller may no longer act on the item. An
    /// item that is no longer open is left as it is.
    /// </summary>
    Task<ErrorOr<Success>> DismissFeedbackAsync(
        string organizationId,
        string userId,
        string turnId,
        string feedbackItemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds daily batches of agent spend for the caller's organization, enough to bring the day's
    /// allowance one batch above what is used, after
    /// <see cref="SendMessageAsync"/> refused with <see cref="EditorAgentErrors.DailySpendUsed"/>.
    /// Does nothing when the organization's batches are no longer used up, as when another user
    /// unlocked one first. Needs the same role and access as sending a message in the conversation.
    /// </summary>
    Task<ErrorOr<Success>> UnlockDailySpendAsync(
        string organizationId,
        string userId,
        string conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops a waiting, queued or running turn. A running turn's spend so far is billed and the
    /// turn ends Cancelled. A turn running on another server is stopped through the runtime and
    /// that server settles it; one still starting there is refused with
    /// <see cref="AgentConversationErrors.StartingElsewhere"/>, to be tried again.
    /// </summary>
    Task<ErrorOr<Success>> CancelTurnAsync(
        string organizationId,
        string userId,
        string turnId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The conversation's first turn still in flight, with the steps it has taken so far, or null
    /// when nothing is in flight: a cheap read for a poll, which re-reads the whole conversation
    /// only when this turn changed. The settled steps of a finished turn are on
    /// <see cref="EditorAgentTurnView.Activity"/>.
    /// </summary>
    Task<ErrorOr<EditorAgentInFlightTurn?>> GetInFlightTurnAsync(
        string organizationId,
        string userId,
        string conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies changes of a turn's proposal to the editor's live workflow, in memory and unsaved.
    /// Each field is checked on its own: a field the user changed since the turn read the workflow
    /// comes back as a conflict carrying both values and is left alone, unless its change id is in
    /// <see cref="EditorAgentAcceptRequest.TakeProposed"/>; the block's other fields still apply.
    /// Null <see cref="EditorAgentAcceptRequest.ChangeIds"/> accepts every change that can be
    /// accepted and is still undecided, except those that send data to an outside address, those
    /// the agent left out of its summary and those on a Library-linked node. A change that sends
    /// data outside applies only when it is the only id named, or is in
    /// <see cref="EditorAgentAcceptRequest.ConfirmOutbound"/>. A refused change (Library link,
    /// failed checks) never applies. Added blocks and sections receive real stable ids. When the
    /// decisions cannot be recorded the apply is taken back and
    /// <see cref="EditorAgentErrors.ProposalChanged"/> returned. Call it on the thread that owns
    /// the workflow; the workflow is not kept.
    /// </summary>
    Task<ErrorOr<EditorAgentApplyResult>> AcceptChangesAsync(
        EditorAgentAcceptRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes back every change of the turn's proposal accepted since its last undo, on the
    /// editor's live workflow: fields return to their pre-apply values, added nodes go, removed
    /// and moved ones come back, and the order of the touched sections is restored. A node the user
    /// changed after the apply is left as it is and reported in
    /// <see cref="EditorAgentUndoResult.Conflicts"/>; its change stays accepted. Refused with
    /// <see cref="EditorAgentErrors.NothingToUndo"/> when nothing is applied. With
    /// <paramref name="changeIds"/>, only those accepted changes and the renumbering that came
    /// with them are taken back, such as one section's; the rest stay applied and can be undone later.
    /// </summary>
    Task<ErrorOr<EditorAgentUndoResult>> UndoChangesAsync(
        string organizationId,
        string userId,
        string turnId,
        Workflow workflow,
        IReadOnlyCollection<string>? changeIds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks changes rejected. Null <paramref name="changeIds"/> rejects every undecided change.
    /// An accepted change is left as it is: undo it first.
    /// </summary>
    Task<ErrorOr<Success>> RejectChangesAsync(
        string organizationId,
        string userId,
        string turnId,
        IReadOnlyList<string>? changeIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// For the editor's save, before it: the version note naming how many accepted agent changes
    /// the save carries ("3 changes proposed by the editor agent"), or null when it carries none.
    /// A change is one block, section, list item or workflow field, however many of its fields
    /// changed; a node whose only change is its execution order does not count. Marks nothing:
    /// call <see cref="CommitSaveNoteAsync"/> once the save has written a new version.
    /// </summary>
    Task<ErrorOr<EditorAgentSaveNote?>> PeekSaveNoteAsync(
        string organizationId,
        string userId,
        string workflowTypeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// After a save that wrote a new version with the note: marks the note's changes saved, so the
    /// next save does not count them again. Not called for a failed save or a deduplicated one, so
    /// the note carries over to the next save. Each open feedback item the agent named for a saved
    /// change, of those its conversation was sent, becomes Addressed in <paramref name="versionId"/>.
    /// </summary>
    Task<ErrorOr<Success>> CommitSaveNoteAsync(
        string organizationId,
        string userId,
        string workflowTypeId,
        EditorAgentSaveNote note,
        string versionId,
        CancellationToken cancellationToken = default);
}

/// <param name="ChangeIds">The changes to apply; null for every acceptable undecided change that
/// sends nothing to an outside address.</param>
/// <param name="Workflow">The editor's live workflow, changed in place.</param>
public sealed record EditorAgentAcceptRequest(
    string OrganizationId,
    string UserId,
    string TurnId,
    IReadOnlyList<string>? ChangeIds,
    Workflow Workflow)
{
    /// <summary>Changes whose conflict the user settled for the agent's value.</summary>
    public IReadOnlyCollection<string> TakeProposed { get; init; } = [];

    /// <summary>
    /// Changes that send data to an outside address the user confirmed one by one, so they may
    /// apply together with other named changes.
    /// </summary>
    public IReadOnlyCollection<string> ConfirmOutbound { get; init; } = [];
}

/// <param name="Text">The note for the version.</param>
/// <param name="DecisionIds">The decisions the note counts, for <see cref="IWorkflowEditorAgentService.CommitSaveNoteAsync"/>.</param>
public sealed record EditorAgentSaveNote(string Text, IReadOnlyList<string> DecisionIds);

/// <param name="UndoToken">Names the proposal whose undo takes these changes back.</param>
public sealed record EditorAgentApplyResult(
    string UndoToken,
    IReadOnlyList<ProposalAppliedChange> Applied,
    IReadOnlyList<ProposalConflict> Conflicts,
    IReadOnlyList<ProposalNotApplied> NotApplied)
{
    /// <summary>Tags the applied changes leave read but unsupplied, as plain sentences.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record EditorAgentUndoResult(IReadOnlyList<string> UndoneChangeIds)
{
    /// <summary>Changes the undo left in place because the user changed them after they were applied.</summary>
    public IReadOnlyList<ProposalConflict> Conflicts { get; init; } = [];
}

/// <summary>
/// A turn's proposal as the review card shows it: every change with its base and new value, its
/// flags and the user's decision, what the agent said it changed, and the checks it still fails.
/// </summary>
/// <param name="CanUndo">True while accepted changes can be taken back as one.</param>
public sealed record EditorAgentProposalView(
    string Id,
    int Rounds,
    IReadOnlyList<EditorAgentChangeView> Changes,
    IReadOnlyList<ProposalSummaryEntry> Summary,
    IReadOnlyList<ProposalCheckError> CheckErrors,
    bool CanUndo)
{
    /// <summary>What a save would ask to confirm, such as agent tags nothing supplies; they refuse nothing.</summary>
    public IReadOnlyList<ProposalCheckError> Warnings { get; init; } = [];

    /// <summary>The agent's account of the whole proposal in plain words; null when it wrote none.</summary>
    public string? Overview { get; init; }

    /// <summary>The accepted changes that no save has kept yet.</summary>
    public int UnsavedAccepted => Changes.Count(static view => view.Decision == EditorAgentChangeDecisionStatus.Accepted && !view.Saved);

    /// <summary>True when changes were accepted and a save has kept every one of them.</summary>
    public bool IsSaved => UnsavedAccepted == 0
        && Changes.Any(static view => view.Decision == EditorAgentChangeDecisionStatus.Accepted);
}

/// <param name="Decision">Null while undecided.</param>
public sealed record EditorAgentChangeView(
    ProposedChange Change,
    EditorAgentChangeDecisionStatus? Decision,
    string? MintedStableId)
{
    /// <summary>True once an editor save wrote a version holding this accepted change.</summary>
    public bool Saved { get; init; }
}

/// <param name="Workflow">The editor's in-memory workflow, unsaved changes and test-run outputs included.</param>
/// <param name="ModelId">One of <see cref="IWorkflowEditorAgentService.ListModelsAsync"/>'s ids; it
/// becomes the conversation's model.</param>
public sealed record EditorAgentMessage(
    string OrganizationId,
    string UserId,
    string ConversationId,
    string Text,
    string ModelId,
    Workflow Workflow)
{
    /// <summary>Past reports the user picked as examples for this message only.</summary>
    public IReadOnlyList<string> ReportIds { get; init; } = [];

    /// <summary>The report feedback items the user chose with the "Use feedback" button.</summary>
    public IReadOnlyList<string> FeedbackItemIds { get; init; } = [];
}

/// <summary>An open feedback item as the "Use feedback" list shows it.</summary>
public sealed record EditorAgentFeedbackOption(
    string Id,
    ReportFeedbackCategory Category,
    string Comment,
    string? QuotedText,
    DateTime CreatedAt);

/// <param name="InputUsdPerMillionTokens">Null when no price is configured for the model.</param>
public sealed record EditorAgentModelOption(
    string ModelId,
    string DisplayName,
    string Capability,
    decimal? InputUsdPerMillionTokens,
    decimal? OutputUsdPerMillionTokens,
    bool IsDefault)
{
    /// <summary>
    /// Which of the platform's default models this is (<see cref="EditorAgentModelCatalog.ADVANCED_TIER"/>
    /// or <see cref="EditorAgentModelCatalog.STANDARD_TIER"/>), or <see cref="EditorAgentModelCatalog.EXPERT_TIER"/>
    /// for the deployment's expert model (<see cref="IniaAi.Configuration.Platform.EditorAgentSettings.ExpertModel"/>); null for a
    /// model offered only because the conversation is on it, or when the runtime allows neither default.
    /// </summary>
    public string? Tier { get; init; }
}

/// <param name="ExpiresAt">When the conversation ends unless the user writes again.</param>
public sealed record EditorAgentConversationView(
    string Id,
    string WorkflowTypeId,
    string ModelId,
    string Effort,
    DateTime CreatedAt,
    DateTime LastActivityAt,
    DateTime ExpiresAt,
    decimal TotalCostUsd,
    IReadOnlyList<EditorAgentTurnView> Turns)
{
    /// <summary>The organization's workflow agent spend today against its daily batches; null when not read.</summary>
    public EditorAgentDailySpend? OrganizationToday { get; init; }
}

/// <summary>A turn in flight as a poll sees it.</summary>
/// <param name="Activity">The steps it has taken so far; null before the first.</param>
public sealed record EditorAgentInFlightTurn(
    int Number,
    AgentConversationTurnStatus Status,
    bool IsWaiting,
    AgentActivityDigest? Activity);

/// <param name="DraftRevision">Which distinct workflow state the turn saw; equal revisions mean
/// the workflow did not change between the turns.</param>
/// <param name="Activity">The settled steps the agent took, once the turn has finished.</param>
/// <param name="IsWaiting">True while the turn waits for an earlier message of its conversation to
/// settle; its status is then Queued.</param>
public sealed record EditorAgentTurnView(
    string Id,
    int Number,
    string UserMessage,
    string? AnswerMarkdown,
    AgentConversationTurnStatus Status,
    string ModelId,
    decimal CostUsd,
    string? Error,
    AgentActivityDigest? Activity,
    int DraftRevision,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    bool IsWaiting)
{
    /// <summary>The changes the agent proposed in this turn; null when it proposed none.</summary>
    public EditorAgentProposalView? Proposal { get; init; }

    /// <summary>The past reports the user sent with this message, in the order picked.</summary>
    public IReadOnlyList<EditorAgentReportSummary> AttachedReports { get; init; } = [];

    /// <summary>How many report feedback items the message was sent with; 0 for an ordinary message.</summary>
    public int FeedbackSent { get; init; }

    /// <summary>The feedback items the agent suggested dismissing in this turn.</summary>
    public IReadOnlyList<EditorAgentDismissSuggestionView> DismissSuggestions { get; init; } = [];
}

/// <summary>
/// Something changed on a turn. <paramref name="Status"/> is null for a progress update that
/// only carries new <paramref name="Activity"/>. A settled status (Completed, Failed, Cancelled)
/// means the panel should re-read the conversation for the answer and cost.
/// </summary>
public sealed record EditorAgentTurnUpdate(
    string OrganizationId,
    string ConversationId,
    int TurnNumber,
    AgentConversationTurnStatus? Status,
    AgentActivityDigest? Activity);

/// <summary>
/// The in-process change feed the panel subscribes to. Raised on a background thread: a Blazor
/// subscriber filters by its own conversation id and marshals with <c>InvokeAsync</c>.
/// </summary>
public interface IEditorAgentNotifications
{
    event EventHandler<EditorAgentTurnUpdate>? TurnUpdated;
}

public static class EditorAgentErrors
{
    public const string DISABLED_CODE = "EditorAgent.Disabled";
    public const string MODEL_NOT_ALLOWED_CODE = "EditorAgent.ModelNotAllowed";
    public const string EMPTY_MESSAGE_CODE = "EditorAgent.EmptyMessage";
    public const string MESSAGE_TOO_LONG_CODE = "EditorAgent.MessageTooLong";
    public const string WORKFLOW_TYPE_NOT_FOUND_CODE = "EditorAgent.WorkflowTypeNotFound";
    public const string FORBIDDEN_CODE = "EditorAgent.Forbidden";
    public const string WORKFLOW_MISMATCH_CODE = "EditorAgent.WorkflowMismatch";
    public const string DRAFT_TOO_LARGE_CODE = "EditorAgent.DraftTooLarge";
    public const string NO_PROPOSAL_CODE = "EditorAgent.NoProposal";
    public const string NOTHING_TO_UNDO_CODE = "EditorAgent.NothingToUndo";
    public const string PROPOSAL_CHANGED_CODE = "EditorAgent.ProposalChanged";
    public const string TOO_MANY_REPORTS_CODE = "EditorAgent.TooManyReports";
    public const string REPORT_NOT_FOUND_CODE = "EditorAgent.ReportNotFound";
    public const string DAILY_SPEND_USED_CODE = "EditorAgent.DailySpendUsed";
    public const string NO_OPEN_FEEDBACK_CODE = "EditorAgent.NoOpenFeedback";
    public const string FEEDBACK_NOT_SUGGESTED_CODE = "EditorAgent.FeedbackNotSuggested";
    public const string TOO_MANY_FEEDBACK_CODE = "EditorAgent.TooManyFeedback";
    public const string FEEDBACK_NOT_FOUND_CODE = "EditorAgent.FeedbackNotFound";
    public const int MAX_MESSAGE_LENGTH = 20000;

    public static Error Disabled => Error.Forbidden(DISABLED_CODE, "The workflow agent is not available on this deployment.");

    public static Error ModelNotAllowed(string modelId) => Error.Validation(
        MODEL_NOT_ALLOWED_CODE, $"The model '{modelId}' is not available on this deployment.");

    public static Error EmptyMessage => Error.Validation(EMPTY_MESSAGE_CODE, "Write a message first.");

    public static Error MessageTooLong => Error.Validation(
        MESSAGE_TOO_LONG_CODE, $"A message can be at most {MAX_MESSAGE_LENGTH} characters.");

    public static Error WorkflowTypeNotFound => Error.NotFound(
        WORKFLOW_TYPE_NOT_FOUND_CODE, "That workflow type does not exist.");

    public static Error Forbidden => Error.Forbidden(
        FORBIDDEN_CODE, "The workflow agent needs the workflow editor role and access to this workflow type.");

    public static Error WorkflowMismatch => Error.Validation(
        WORKFLOW_MISMATCH_CODE, "The open workflow is not the workflow this conversation is about.");

    public static Error NoProposal => Error.NotFound(
        NO_PROPOSAL_CODE, "That message proposed no changes, or it is not yours.");

    public static Error ProposalChanged => Error.Conflict(
        PROPOSAL_CHANGED_CODE, "These changes were decided elsewhere at the same moment. Reload the conversation and try again.");

    public static Error NothingToUndo => Error.Conflict(
        NOTHING_TO_UNDO_CODE, "None of this message's changes are applied.");

    public static Error TooManyReports => Error.Validation(
        TOO_MANY_REPORTS_CODE, $"Pick at most {EditorAgentReports.MAX_PICKED} reports for one message.");

    public static Error ReportNotFound => Error.NotFound(
        REPORT_NOT_FOUND_CODE, "One of the picked reports is not a report of this workflow in your organization.");

    public static Error NoOpenFeedback => Error.NotFound(
        NO_OPEN_FEEDBACK_CODE, "There is no open feedback on this workflow's reports.");

    public static Error TooManyFeedback => Error.Validation(
        TOO_MANY_FEEDBACK_CODE, $"Choose at most {EditorAgentFeedback.MAX_SENT} feedback items for one message.");

    public static Error FeedbackNotSuggested => Error.NotFound(
        FEEDBACK_NOT_SUGGESTED_CODE, "The agent did not suggest dismissing that feedback, or the message is not yours.");

    public static Error FeedbackNotFound => Error.NotFound(
        FEEDBACK_NOT_FOUND_CODE, "That feedback is not on this workflow's reports, or it was already deleted.");

    public const string USED_USD_METADATA_KEY = "usedUsd";
    public const string BATCH_USD_METADATA_KEY = "batchUsd";

    public static Error DailySpendUsed(decimal usedUsd, decimal batchUsd) => Error.Conflict(
        DAILY_SPEND_USED_CODE,
        DailySpendText.Warning(usedUsd, batchUsd),
        new Dictionary<string, object> { [USED_USD_METADATA_KEY] = usedUsd, [BATCH_USD_METADATA_KEY] = batchUsd });

    public static Error DraftTooLarge(int megabytes) => Error.Validation(
        DRAFT_TOO_LARGE_CODE, $"The workflow is larger than the {megabytes} MB the agent can read.");
}

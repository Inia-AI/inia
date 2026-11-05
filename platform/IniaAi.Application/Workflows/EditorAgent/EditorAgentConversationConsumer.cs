using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Queries.Workflows.Versions;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;
using IniaAi.Configuration.Platform;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.ReportFeedback;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Proposals;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.WorkflowVersions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TokenExecutionContext = IniaAi.Domain.Entities.TokenUsage.Enums.ExecutionContext;

namespace IniaAi.Application.Workflows.EditorAgent;

/// <summary>
/// The workflow editor's part of a conversation turn: its policy, its billing, what a turn reads,
/// staged from the draft the turn points at, and the stop gate that turns the agent's edited
/// workflow into a checked proposal. The shared conversation layer does the rest.
/// </summary>
public sealed class EditorAgentConversationConsumer : IAgentConversationConsumer
{
    public const string BILLING_DESCRIPTION = "Workflow editor agent message";

    private const int VERSION_LIST_SIZE = 50;

    private readonly IUserDataUnitOfWork _userData;
    private readonly ISender _sender;
    private readonly EditorAgentSettings _settings;
    private readonly IAgentRunLimitsProvider _limits;
    private readonly EditorAgentProposalGate _gate;
    private readonly EditorAgentReports _reports;
    private readonly EditorAgentReportDocuments _reportDocuments;
    private readonly EditorAgentFeedback _feedback;

    public EditorAgentConversationConsumer(
        IUserDataUnitOfWork userData,
        ISender sender,
        IOptions<EditorAgentSettings> settings,
        IAgentRunLimitsProvider limits,
        EditorAgentProposalGate gate,
        EditorAgentReports reports,
        EditorAgentReportDocuments reportDocuments,
        EditorAgentFeedback feedback)
    {
        _userData = userData;
        _sender = sender;
        _settings = settings.Value;
        _limits = limits;
        _gate = gate;
        _reports = reports;
        _reportDocuments = reportDocuments;
        _feedback = feedback;
    }

    public string Purpose => WorkflowEditorAgentService.PURPOSE;

    public AgentConversationPolicy Policy => new(
        _settings.RetentionDays,
        _settings.MaxRunningTurnsPerUser,
        TimeSpan.FromMinutes(_settings.DeadlineMinutesPerMessage) + TimeSpan.FromSeconds(_settings.AdmissionPatienceSeconds));

    public AgentConversationBilling Billing(AgentConversation conversation)
    {
        return new AgentConversationBilling(
            UsageType.LlmEditorAgentChat,
            TokenExecutionContext.WorkflowEditorAgent,
            BILLING_DESCRIPTION)
        {
            WorkflowTypeId = conversation.SubjectId,
        };
    }

    public async Task<AgentConversationTurnRequest> StageTurnAsync(
        AgentConversationTurn turn,
        AgentConversation conversation,
        CancellationToken cancellationToken)
    {
        string draftId = turn.ContextRef
            ?? throw new InvalidOperationException($"Turn '{turn.Id}' has no draft");
        string? previousDraftId = await _userData.Repository<AgentConversationTurn>()
            .GetAll()
            .AsNoTracking()
            .Where(row => row.ConversationId == conversation.Id && row.Number < turn.Number)
            .OrderByDescending(static row => row.Number)
            .Select(static row => row.ContextRef)
            .FirstOrDefaultAsync(cancellationToken);

        List<EditorAgentDraft> drafts = await _userData.Repository<EditorAgentDraft>()
            .GetAll()
            .AsNoTracking()
            .Where(row => row.Id == draftId || row.Id == previousDraftId)
            .ToListAsync(cancellationToken);

        EditorAgentDraft draft = drafts.SingleOrDefault(row => row.Id == draftId)
            ?? throw new InvalidOperationException($"The draft of turn '{turn.Id}' does not exist");
        EditorAgentDraft? previous = drafts.SingleOrDefault(row => row.Id == previousDraftId && row.Id != draftId);

        string workflowJson = draft.WorkflowJson;
        Workflow workflow = Workflow.FromExportJson(workflowJson)
            ?? throw new InvalidOperationException($"The draft of turn '{turn.Id}' is not a workflow");

        (IReadOnlyList<WorkflowVersionSummaryDto> versions, bool moreVersions) =
            await VersionsAsync(conversation, draft.WorkflowTypeId, cancellationToken);

        IReadOnlyList<EditorAgentBlockChange>? changes = previousDraftId is null
            ? null
            : previous is null
                ? []
                : EditorAgentStaging.ChangedBlocks(previous.WorkflowJson, workflowJson);

        AgentRunLimits? ceilings = _limits.Enforceable;
        long deadlineMs = (long)TimeSpan.FromMinutes(_settings.DeadlineMinutesPerMessage).TotalMilliseconds;
        decimal maxBudgetUsd = _settings.MaxBudgetUsdPerMessage;
        int maxTurns = _settings.MaxTurnsPerMessage;
        if (ceilings is not null)
        {
            deadlineMs = Math.Min(deadlineMs, (long)ceilings.DeadlineMs.Ceiling);
            maxBudgetUsd = Math.Min(maxBudgetUsd, ceilings.MaxBudgetUsd.Ceiling);
            maxTurns = Math.Min(maxTurns, (int)ceilings.MaxTurns.Ceiling);
        }

        string organizationId = conversation.OrganizationId;
        IReadOnlyList<EditorAgentReportSummary> reports = await _reports.ListAsync(organizationId, draft.WorkflowTypeId, cancellationToken);
        IReadOnlyList<EditorAgentWorkflowTypeOption> types = await _reports.AccessibleTypesAsync(organizationId, draft.WorkflowTypeId, cancellationToken);
        string typeName = types.FirstOrDefault(type => type.Id == draft.WorkflowTypeId)?.Name ?? draft.WorkflowTypeId;
        EditorAgentOpenWorkflowType openType = await _reports.OpenWorkflowTypeAsync(draft.WorkflowTypeId, cancellationToken);
        List<AgentRunDocument> turnDocuments =
        [
            .. EditorAgentStaging.Documents(workflowJson, workflow, versions, moreVersions),
            EditorAgentReports.ListDocument(typeName, reports),
        ];
        AgentRunSkillFile guide = EditorAgentStaging.GuideSkill();
        EditorAgentReportBudget budget = EditorAgentReportBudget.ForMessage(
            (long)_settings.MaxReportDocumentsMegabytesPerMessage * EditorAgentReportFiles.BYTES_PER_MEGABYTE, turnDocuments, [guide]);
        (IReadOnlyList<EditorAgentReportSummary> picked, IReadOnlyList<EditorAgentReportEvidence> pickedEvidence) =
            await PickedAsync(turn, organizationId, openType, [.. types.Select(static type => type.Id)], budget, cancellationToken);

        List<ReportFeedbackItem> sentFeedback = await _feedback.SentInConversationAsync(conversation.Id, cancellationToken);
        Dictionary<string, string> feedbackRuns = EditorAgentFeedback.RunOrganizations(sentFeedback);
        StagedFeedback feedback = await StageFeedbackAsync(
            turn, typeName, openType, picked.Select(static report => report.Id).ToHashSet(StringComparer.Ordinal), budget, cancellationToken);
        List<EditorAgentReportEvidence> evidence = [.. pickedEvidence, .. feedback.Evidence];
        if (feedback.Document is AgentRunDocument feedbackDocument)
        {
            turnDocuments.Add(feedbackDocument);
        }

        List<EditorAgentReportSummary> known = [.. reports];
        known.AddRange(evidence.Select(static report => report.Summary).Where(report => known.All(listed => listed.Id != report.Id)));
        var leaks = new EditorAgentReportLeaks(evidence, reports);
        leaks.AddTexts(EditorAgentFeedback.Texts(sentFeedback));
        await AddEarlierReportsAsync(turn, organizationId, feedbackRuns, leaks, cancellationToken);
        var fetch = new EditorAgentReportFetch(
            _reports,
            _reportDocuments,
            budget,
            _userData,
            turn,
            organizationId,
            draft.WorkflowTypeId,
            leaks,
            evidence.Select(static report => report.Summary.Id),
            feedbackRuns);
        HashSet<string> sentIds = [.. sentFeedback.Select(static item => item.Id)];

        return new AgentConversationTurnRequest(
            turn.Id,
            EditorAgentStaging.TurnPrompt(turn.UserMessage, changes) + EditorAgentStaging.PickedReportsNote(picked, openType) + feedback.Note)
        {
            SystemPrompt = EditorAgentStaging.SystemPrompt,
            Documents =
            [
                .. turnDocuments,
                .. evidence.SelectMany(static report => report.Documents),
            ],
            SkillFiles = [guide],
            DeadlineMs = deadlineMs,
            MaxBudgetUsd = maxBudgetUsd,
            MaxTurns = maxTurns,
            AdmissionPatience = TimeSpan.FromSeconds(_settings.AdmissionPatienceSeconds),
            ResumeSession = _settings.ResumeSessions,
            Billing = Billing(conversation),
            ReviewRound = (round, token) => ReviewAsync(turn, draft, fetch, leaks, sentIds, round, token),
            MaxRounds = EditorAgentProposalGate.MAX_ROUNDS + EditorAgentReportFetch.MAX_FETCH_ROUNDS,
            LiveActivity = LiveActivity(workflow, known),
        };
    }

    /// <summary>
    /// A turn past the retention window keeps its proposal, but a change refused as report text
    /// loses its text: the report text is what the refusal kept out of the workflow.
    /// </summary>
    public async Task PruneTurnsAsync(IReadOnlyList<string> turnIds, CancellationToken cancellationToken)
    {
        List<EditorAgentProposal> rows = await _userData.Repository<EditorAgentProposal>()
            .GetAll()
            .Where(row => turnIds.Contains(row.TurnId))
            .ToListAsync(cancellationToken);

        bool pruned = false;
        foreach (EditorAgentProposal row in rows)
        {
            WorkflowProposal proposal = row.Proposal;
            List<int> refused = [.. proposal.Changes
                .Select(static (change, index) => (change, index))
                .Where(static item => item.change.Refusal == ProposalRefusal.ReportText && item.change.NewJson is not null)
                .Select(static item => item.index)];
            if (refused.Count == 0)
            {
                continue;
            }

            foreach (int index in refused)
            {
                proposal.Changes[index] = proposal.Changes[index] with { NewJson = null };
            }

            row.Proposal = proposal;
            row.Version = EditorAgentProposal.NewVersion();
            row.UpdatedAt = DateTime.UtcNow;
            pruned = true;
        }

        if (pruned)
        {
            _ = await _userData.CompleteAsync(cancellationToken);
        }
    }

    /// <summary>
    /// The panel's steps: named here from the workflow and its reports, so none waits for the
    /// narration tidier, and refreshed at the editor's own pace while the message runs.
    /// </summary>
    private AgentRunLiveActivityOptions LiveActivity(Workflow workflow, IReadOnlyList<EditorAgentReportSummary> reports)
    {
        return new AgentRunLiveActivityOptions
        {
            PublishInterval = _settings.LiveStepsIntervalMilliseconds > 0
                ? TimeSpan.FromMilliseconds(_settings.LiveStepsIntervalMilliseconds)
                : null,
            Projection = new AgentActivityProjectionOptions
            {
                StepLabeller = new EditorAgentStepLabeller(workflow, reports),
                RemarksBetweenToolCallsOnly = true,
            },
        };
    }

    /// <summary>
    /// A report request goes first; otherwise the stop gate reviews the round, counting only its
    /// own rounds, so report fetches never take a round the gate needs. The gate refuses any
    /// change that carries text from a report the conversation staged.
    /// </summary>
    private async Task<AgentConversationRoundReview> ReviewAsync(
        AgentConversationTurn turn,
        EditorAgentDraft draft,
        EditorAgentReportFetch fetch,
        EditorAgentReportLeaks leaks,
        IReadOnlySet<string> sentFeedbackIds,
        AgentConversationRound round,
        CancellationToken cancellationToken)
    {
        await RecordDismissalsAsync(turn, round, sentFeedbackIds, cancellationToken);
        if (await fetch.TryFetchAsync(round, cancellationToken) is AgentConversationRoundReview fetched)
        {
            return fetched;
        }

        int gateRound = round.Round - fetch.Rounds;
        AgentConversationRound forGate = round with
        {
            Round = gateRound,
            IsLastRound = round.IsLastRound || gateRound >= EditorAgentProposalGate.MAX_ROUNDS,
        };

        return fetch.WithFetched(await _gate.ReviewAsync(turn, draft, forGate, cancellationToken, leaks));
    }

    /// <summary>
    /// What earlier turns of the conversation staged stays report text for this one: a resumed
    /// session still holds those reports. Every report an earlier turn picked or opened is read
    /// again for its text alone. The agent's earlier answers are not report text: a passage an
    /// answer quoted from a report is caught by that report's own text.
    /// </summary>
    private async Task AddEarlierReportsAsync(
        AgentConversationTurn turn,
        string organizationId,
        IReadOnlyDictionary<string, string> feedbackRuns,
        EditorAgentReportLeaks leaks,
        CancellationToken cancellationToken)
    {
        List<EditorAgentTurnReports> earlier = await _userData.Repository<EditorAgentTurnReports>()
            .GetAll()
            .AsNoTracking()
            .Where(row => row.ConversationId == turn.ConversationId
                && row.TurnId != turn.Id
                && (row.PickedExecutionIds != string.Empty || row.FetchedExecutionIds != string.Empty))
            .ToListAsync(cancellationToken);
        if (earlier.Count == 0)
        {
            return;
        }

        foreach (string id in earlier.SelectMany(static row => row.Picked.Concat(row.Fetched)).Distinct(StringComparer.Ordinal))
        {
            string owner = feedbackRuns.GetValueOrDefault(id) ?? organizationId;
            if (await _reports.StagedEarlierAsync(owner, id, cancellationToken) is EditorAgentReportEvidence report)
            {
                leaks.Add(await _reportDocuments.WithTextsAsync(owner, report, cancellationToken));
            }
        }
    }

    /// <summary>
    /// The reports the user picked for the message that are still the organization's runs of a
    /// type it has access to, at most <see cref="EditorAgentReports.MAX_PICKED"/>, with their
    /// staged files and documents.
    /// </summary>
    private async Task<(IReadOnlyList<EditorAgentReportSummary> Picked, IReadOnlyList<EditorAgentReportEvidence> Evidence)> PickedAsync(
        AgentConversationTurn turn,
        string organizationId,
        EditorAgentOpenWorkflowType openWorkflowType,
        IReadOnlyCollection<string> workflowTypeIds,
        EditorAgentReportBudget budget,
        CancellationToken cancellationToken)
    {
        string? pickedIds = await _userData.Repository<EditorAgentTurnReports>()
            .GetAll()
            .AsNoTracking()
            .Where(row => row.TurnId == turn.Id && row.ConversationId == turn.ConversationId)
            .Select(static row => row.PickedExecutionIds)
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrEmpty(pickedIds))
        {
            return ([], []);
        }

        IReadOnlyList<string> owned = await _reports.OwnedIdsAsync(
            organizationId,
            workflowTypeIds,
            [.. pickedIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Take(EditorAgentReports.MAX_PICKED)],
            cancellationToken);

        var picked = new List<EditorAgentReportSummary>();
        var evidence = new List<EditorAgentReportEvidence>();
        foreach (string id in owned)
        {
            if (await _reports.EvidenceAsync(organizationId, workflowTypeIds, id, cancellationToken, budget, openWorkflowType) is EditorAgentReportEvidence found)
            {
                found = await _reportDocuments.AddAsync(organizationId, found, budget, cancellationToken);
                evidence.Add(found);
                picked.Add(found.Summary);
            }
        }

        return (picked, evidence);
    }

    /// <summary>
    /// The feedback the user sent with the message, when they pressed "Use feedback": the items
    /// still open, listed in <see cref="EditorAgentFeedback.DOCUMENT"/>, and the runs holding the
    /// most of them staged whole in the report slots the user's picks leave, each from its own
    /// organization. The staged runs count as opened in the turn, so later turns check against
    /// them too.
    /// </summary>
    private async Task<StagedFeedback> StageFeedbackAsync(
        AgentConversationTurn turn,
        string workflowTypeName,
        EditorAgentOpenWorkflowType openWorkflowType,
        IReadOnlySet<string> picked,
        EditorAgentReportBudget budget,
        CancellationToken cancellationToken)
    {
        EditorAgentTurnFeedback? row = await _userData.Repository<EditorAgentTurnFeedback>()
            .GetAll()
            .AsNoTracking()
            .SingleOrDefaultAsync(existing => existing.TurnId == turn.Id && existing.ConversationId == turn.ConversationId, cancellationToken);
        if (row is null || row.Sent.Count == 0)
        {
            return StagedFeedback.None;
        }

        List<ReportFeedbackItem> open = [.. (await _feedback.ByIdsAsync(row.Sent, cancellationToken))
            .Where(static item => item.Status == ReportFeedbackStatus.Open)];
        var evidence = new List<EditorAgentReportEvidence>();
        IEnumerable<EditorAgentFeedbackRun> runs = EditorAgentFeedback
            .RunsToStage(open.Where(item => !picked.Contains(item.WorkflowExecutionId)), EditorAgentReports.MAX_PICKED - picked.Count);
        foreach (EditorAgentFeedbackRun run in runs)
        {
            if (await _reports.EvidenceAsync(run.OrganizationId, [openWorkflowType.Id], run.ExecutionId, cancellationToken, budget, openWorkflowType)
                is EditorAgentReportEvidence found)
            {
                evidence.Add(await _reportDocuments.AddAsync(run.OrganizationId, found, budget, cancellationToken));
            }
        }

        if (evidence.Count > 0)
        {
            await RecordStagedRunsAsync(turn, evidence.Select(static report => report.Summary.Id), cancellationToken);
        }

        HashSet<string> staged = [.. picked, .. evidence.Select(static report => report.Summary.Id)];
        return new StagedFeedback(
            EditorAgentFeedback.Document(workflowTypeName, open, Math.Max(row.OpenCount, open.Count), staged),
            evidence,
            EditorAgentFeedback.Note(open.Count));
    }

    private async Task RecordStagedRunsAsync(AgentConversationTurn turn, IEnumerable<string> executionIds, CancellationToken cancellationToken)
    {
        EditorAgentTurnReports? row = await _userData.Repository<EditorAgentTurnReports>()
            .GetAll()
            .SingleOrDefaultAsync(existing => existing.TurnId == turn.Id, cancellationToken);
        if (row is null)
        {
            row = new EditorAgentTurnReports { TurnId = turn.Id, ConversationId = turn.ConversationId };
            _userData.Repository<EditorAgentTurnReports>().Add(row);
        }

        row.FetchedExecutionIds = string.Join(',', row.Fetched.Concat(executionIds).Distinct(StringComparer.Ordinal));
        _ = await _userData.CompleteAsync(cancellationToken);
    }

    /// <summary>
    /// The agent's suggestions to dismiss feedback items, from the dismiss file it wrote this turn,
    /// kept for the items the conversation was sent. A file identical to one already recorded in
    /// the conversation is that file, carried over or seen in an earlier round.
    /// </summary>
    private async Task RecordDismissalsAsync(
        AgentConversationTurn turn,
        AgentConversationRound round,
        IReadOnlySet<string> sentFeedbackIds,
        CancellationToken cancellationToken)
    {
        AgentRunRecarriedFile? file = round.OutFiles.FirstOrDefault(static file => file.Path == EditorAgentFeedback.DISMISS_FILE);
        if (file is null || sentFeedbackIds.Count == 0)
        {
            return;
        }

        bool recorded = await _userData.Repository<EditorAgentTurnFeedback>()
            .GetAll()
            .AsNoTracking()
            .AnyAsync(row => row.ConversationId == turn.ConversationId && row.DismissFileSha256 == file.Sha256, cancellationToken);
        if (recorded || EditorAgentFeedback.ReadDismissals(file.Content.Span, sentFeedbackIds) is not { } suggestions)
        {
            return;
        }

        EditorAgentTurnFeedback? row = await _userData.Repository<EditorAgentTurnFeedback>()
            .GetAll()
            .SingleOrDefaultAsync(existing => existing.TurnId == turn.Id, cancellationToken);
        if (row is null)
        {
            row = new EditorAgentTurnFeedback { TurnId = turn.Id, ConversationId = turn.ConversationId };
            _userData.Repository<EditorAgentTurnFeedback>().Add(row);
        }

        row.DismissSuggestionsJson = suggestions.Count == 0 ? null : EditorAgentFeedback.DismissalsJson(suggestions);
        row.DismissFileSha256 = file.Sha256;
        _ = await _userData.CompleteAsync(cancellationToken);
    }

    /// <summary>What a turn stages of the feedback it was sent; nothing for an ordinary message.</summary>
    private sealed record StagedFeedback(AgentRunDocument? Document, IReadOnlyList<EditorAgentReportEvidence> Evidence, string Note)
    {
        public static StagedFeedback None { get; } = new(null, [], string.Empty);
    }

    private async Task<(IReadOnlyList<WorkflowVersionSummaryDto> Versions, bool More)> VersionsAsync(
        AgentConversation conversation,
        string workflowTypeId,
        CancellationToken cancellationToken)
    {
        ErrorOr<WorkflowHistoryPageDto> page = await _sender.Send(
            new GetWorkflowHistoryQuery(conversation.OrganizationId, workflowTypeId, VERSION_LIST_SIZE),
            cancellationToken);

        return page.IsError ? ([], false) : (page.Value.Versions, page.Value.HasMore);
    }
}

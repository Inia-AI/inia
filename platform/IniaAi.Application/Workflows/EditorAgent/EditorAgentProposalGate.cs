using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IniaAi.Application.Common.Workflows;
using IniaAi.Common;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Common.Resources;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Proposals;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Configuration.Platform;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.Application.Workflows.EditorAgent;

/// <summary>
/// The stop gate at the end of each round of an editor agent turn. When the agent wrote
/// <c>out/workflow.json</c>, the gate builds the change list against the draft the turn read, runs
/// every save check on the result and records the proposal with the turn. While checks fail and a
/// round is left, it sends the agent the errors to fix within the same turn; after the last round,
/// or when a round brings back the same errors as the one before, the remaining errors stay on the
/// proposal and the changes they trace to cannot be accepted. Every change is also applied to a copy
/// of the draft the way the editor will apply it, so a change the editor cannot apply goes back to
/// the agent instead of reaching the user.
/// </summary>
public sealed class EditorAgentProposalGate
{
    public const int MAX_ROUNDS = 3;
    public const string WORKFLOW_FILE = "workflow.json";
    public const string SUMMARY_FILE = "summary.json";

    private const string GATE_RESOURCE = "editor-agent-gate.md";
    private const string ROUND_SLOT = "{round}";
    private const string ROUNDS_SLOT = "{rounds}";
    private const string ERRORS_SLOT = "{errors}";
    private const int MAX_ERRORS_SHOWN = 40;
    private const int MAX_ERROR_LENGTH = 600;
    public const string REPORT_TEXT_REFUSAL = "This change contains text from a client report.";
    private const string REPORT_TEXT_ERROR =
        "The change to {0} contains {1}. The workflow is shared with other organizations: describe the pattern in general terms and copy nothing from a report.";
    private const string LIBRARY_LINK_REFUSAL =
        "The agent changed a Library link. Library links are managed from the Library, so this change cannot be accepted.";
    private const string ELLIPSIS = "...";
    private const string MISSING_SUMMARY = "out/summary.json is missing. Write it with one entry per block or section you meant to change.";
    private const string UNREADABLE_SUMMARY = "out/summary.json is not the expected shape: {0}";
    private const string UNREADABLE_WORKFLOW = "out/workflow.json is not a readable workflow: {0}";
    private const string TOO_MANY_CHANGES = "out/workflow.json changes {0} fields; propose at most {1} at a time.";
    private const string TOO_MANY_ENTRIES = "out/summary.json has more than {0} entries.";
    private const string PREVIOUS_WORKFLOW_DOCUMENT = "previous-attempt.json";
    private const string PREVIOUS_SUMMARY_DOCUMENT = "previous-attempt-summary.json";
    private const string PREVIOUS_ANSWER_DOCUMENT = "previous-attempt-answer.md";
    private const string JSON_MEDIA_TYPE = "application/json";
    private const string MARKDOWN_MEDIA_TYPE = "text/markdown";
    private const int MAX_CHANGES = 1000;
    private const int MAX_SUMMARY_ENTRIES = 500;
    private const int MAX_ERRORS_STORED = 200;
    private const int BYTES_PER_MEGABYTE = 1024 * 1024;
    private const int MAX_SINGLE_APPLY_ATTEMPTS = 50;
    private const string PROPERTIES_PREFIX = "Properties.";
    private const string DELETED_LIST_ERROR =
        "{0}: you deleted the setting `{1}`, which is a list. A list setting cannot be deleted. To clear it, set it to an empty list `[]`. To keep it, leave the existing value in place.";
    private const string DELETED_VALUE_ERROR =
        "{0}: you deleted the setting `{1}`, which must always have a value. Set it to a valid value, or leave the existing value in place.";
    private const string DELETED_SETTING_ERROR =
        "{0}: you deleted the setting `{1}`, and the editor cannot apply that. Give it a valid value instead (an empty list `[]` clears a list), or leave the existing value in place.";
    private const string UNAPPLIABLE_CHANGE_ERROR =
        "{0}: the editor could not apply your change to `{1}` ({2}). Check that the value matches what this setting takes, or leave the existing value in place.";
    private const string UNAPPLIABLE_TOGETHER_ERROR =
        "{0}: your changes could not be applied together ({1}). Check that the changes to this block agree with each other.";
    private const string THE_WORKFLOW = "The workflow";

    private static readonly Lazy<string> _gateTemplate = new(() =>
    {
        using var reader = new StreamReader(typeof(EditorAgentProposalGate).Assembly.ReadResource(GATE_RESOURCE));
        return reader.ReadToEnd().Trim();
    });

    private readonly IUserDataUnitOfWork _userData;
    private readonly ICoreUnitOfWork _core;
    private readonly WorkflowSaveChecks _checks;
    private readonly EditorAgentSettings _settings;
    private readonly ILogger<EditorAgentProposalGate> _logger;
    private readonly Dictionary<string, DraftContext> _drafts = new(StringComparer.Ordinal);
    private Workflow? _savedTemplate;
    private bool _savedTemplateRead;
    private string? _previousErrors;

    public EditorAgentProposalGate(
        IUserDataUnitOfWork userData,
        ICoreUnitOfWork core,
        WorkflowSaveChecks checks,
        IOptions<EditorAgentSettings> settings,
        ILogger<EditorAgentProposalGate> logger)
    {
        _userData = userData;
        _core = core;
        _checks = checks;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<AgentConversationRoundReview> ReviewAsync(
        AgentConversationTurn turn,
        EditorAgentDraft draft,
        AgentConversationRound round,
        CancellationToken cancellationToken,
        EditorAgentReportLeaks? leaks = null)
    {
        AgentRunRecarriedFile? workflowFile = round.OutFiles.FirstOrDefault(static file => file.Path == WORKFLOW_FILE);
        if (workflowFile is null)
        {
            return AgentConversationRoundReview.Finish;
        }

        string sha256 = Convert.ToHexStringLower(SHA256.HashData(workflowFile.Content.Span));
        if (await CarriedOverAsync(turn, sha256, cancellationToken))
        {
            return AgentConversationRoundReview.Finish;
        }

        var errors = new List<ProposalCheckError>();
        DraftContext context = ContextOf(draft);
        string proposedJson = Encoding.UTF8.GetString(workflowFile.Content.Span);
        EditorAgentSummaryFile summary = ReadSummary(round.OutFiles, errors);
        WorkflowProposal proposal = Build(context, proposedJson, workflowFile.Content.Length, summary.Changes, errors, out JObject? proposedRoot);
        proposal.Overview = summary.Overview;

        if (proposal.Changes.Count == 0 && proposedRoot is not null)
        {
            return AgentConversationRoundReview.Finish;
        }

        var warnings = new List<ProposalCheckError>();
        if (proposedRoot is not null)
        {
            await CheckAsync(draft, turn.UserId, context, proposedJson, errors, warnings, cancellationToken);
            context.Workflow ??= Read(context.Json);
            EditorAgentOutboundFlow.Flag(proposal, context.Workflow, Read(proposedJson));
        }

        IReadOnlyList<(ProposedChange Change, string What)> leaked = leaks is null || proposedRoot is null || context.Root is null
            ? []
            : leaks.Find(proposal.Changes, context.AuthoredText ??= EditorAgentWorkflowText.Authored(context.Root));

        if (proposedRoot is not null)
        {
            TrialApply(turn, context, proposal, new HashSet<ProposedChange>(leaked.Select(static pair => pair.Change)), errors);
        }

        proposal.Rounds = round.Round;
        proposal.Warnings.AddRange(warnings.Take(MAX_ERRORS_STORED));
        WorkflowProposalBuilder.AttachCheckErrors(proposal, proposedRoot, Capped(errors));
        foreach (ProposedChange change in proposal.Changes.Where(static change => change.Refusal == ProposalRefusal.LibraryLink))
        {
            change.RefusedReason = LIBRARY_LINK_REFUSAL;
        }

        foreach ((ProposedChange change, string what) in leaked)
        {
            var error = new ProposalCheckError(
                ProposalChecks.REPORT_TEXT,
                string.Format(CultureInfo.InvariantCulture, REPORT_TEXT_ERROR, change.Name ?? change.Field ?? change.TargetKey, what),
                change.Scope == ProposedChangeScope.Block ? change.TargetKey : null);
            errors.Add(error);
            proposal.CheckErrors.Add(error);
            change.Refusal = ProposalRefusal.ReportText;
            change.RefusedReason = REPORT_TEXT_REFUSAL;
        }

        await SaveAsync(turn, draft, sha256, proposal, cancellationToken);

        string errorsSeen = $"{turn.Id}\n{string.Join('\n', errors.Select(static error => $"{error.Check}|{error.Message}").Order(StringComparer.Ordinal))}";
        bool repeated = errors.Count > 0 && errorsSeen == _previousErrors;
        _previousErrors = errorsSeen;
        if (repeated)
        {
            _logger.LogWarning(
                "Editor agent turn {TurnId} brought back the same {Errors} check error(s) in round {Round}, so it stops here",
                turn.Id, errors.Count, round.Round);
        }

        if (errors.Count == 0 || round.IsLastRound || repeated)
        {
            _logger.LogInformation(
                "Editor agent turn {TurnId} proposes {Changes} change(s) after {Rounds} round(s), {Errors} check error(s) left",
                turn.Id, proposal.Changes.Count, round.Round, errors.Count);

            return AgentConversationRoundReview.Finish;
        }

        return AgentConversationRoundReview.Continue(
            GateMessage(errors, round.Round, MAX_ROUNDS),
            PreviousAttempt(workflowFile, round));
    }

    /// <summary>
    /// Applies the changes to a copy of the draft as the editor's preview and accept will. A deleted
    /// list, or a deleted setting that must always have a value, is refused before trying: it may
    /// not throw here and still break the editor later. When the rest fail together, each one is
    /// tried alone to find the one to name, and what is left is tried again; a failure no single
    /// change explains is laid on every block those changes touch, so none of them reaches the user.
    /// </summary>
    private void TrialApply(
        AgentConversationTurn turn,
        DraftContext context,
        WorkflowProposal proposal,
        IReadOnlySet<ProposedChange> refused,
        List<ProposalCheckError> errors)
    {
        List<ProposedChange> changes = [.. proposal.Changes.Where(change => change.CanAccept && !refused.Contains(change))];
        var named = new List<ProposalCheckError>();
        var blocked = new HashSet<ProposedChange>();
        foreach (ProposedChange change in changes)
        {
            if (DeletedRequiredSetting(context.Workflow, change) is string message)
            {
                named.Add(new ProposalCheckError(ProposalChecks.APPLY, message, BlockKeyOf(change)));
                _ = blocked.Add(change);
            }
        }

        List<ProposedChange> rest = [.. changes.Where(change => !blocked.Contains(change))];
        if (rest.Count > 0 && TryApply(context.Json, rest) is Exception together)
        {
            _logger.LogWarning(together, "Editor agent turn {TurnId} proposed changes the editor cannot apply", turn.Id);

            foreach (ProposedChange change in rest.Where(static change => !change.Incidental).Take(MAX_SINGLE_APPLY_ATTEMPTS))
            {
                if (TryApply(context.Json, [change]) is Exception alone)
                {
                    named.Add(new ProposalCheckError(ProposalChecks.APPLY, ApplyMessage(change, alone), BlockKeyOf(change)));
                    _ = blocked.Add(change);
                }
            }

            List<ProposedChange> left = [.. rest.Where(change => !blocked.Contains(change))];
            if (left.Count > 0 && TryApply(context.Json, left) is Exception stillFails)
            {
                named.AddRange(left
                    .Where(static change => !change.Incidental)
                    .GroupBy(static change => BlockKeyOf(change) ?? Where(change), StringComparer.Ordinal)
                    .Select(group => new ProposalCheckError(
                        ProposalChecks.APPLY,
                        string.Format(CultureInfo.InvariantCulture, UNAPPLIABLE_TOGETHER_ERROR, Where(group.First()), stillFails.Message),
                        BlockKeyOf(group.First()))));
            }
        }

        errors.AddRange(named);
    }

    /// <summary>
    /// The error for a block setting the agent deleted when the editor needs a value there: a list,
    /// or a number or switch that cannot be empty. Null for anything else.
    /// </summary>
    private static string? DeletedRequiredSetting(Workflow? draft, ProposedChange change)
    {
        if (change is not { Scope: ProposedChangeScope.Block, Kind: ProposedChangeKind.FieldEdited, NewJson: null, Field: string field }
            || !field.StartsWith(PROPERTIES_PREFIX, StringComparison.Ordinal)
            || draft?.Sections.SelectMany(static section => section.Blocks).FirstOrDefault(block => block.StableBlockId == change.TargetKey) is not Block block)
        {
            return null;
        }

        string name = field[PROPERTIES_PREFIX.Length..].Split('.')[0];
        Type? type = block.Properties.GetType().GetProperties()
            .FirstOrDefault(property => string.Equals(
                property.GetCustomAttributes(typeof(JsonPropertyAttribute), inherit: true).OfType<JsonPropertyAttribute>().FirstOrDefault()?.PropertyName ?? property.Name,
                name,
                StringComparison.Ordinal))?.PropertyType;
        if (type is null)
        {
            return null;
        }

        if (type != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
        {
            return string.Format(CultureInfo.InvariantCulture, DELETED_LIST_ERROR, Where(change), field);
        }

        return type.IsValueType && Nullable.GetUnderlyingType(type) is null
            ? string.Format(CultureInfo.InvariantCulture, DELETED_VALUE_ERROR, Where(change), field)
            : null;
    }

    private static Exception? TryApply(string draftJson, IReadOnlyList<ProposedChange> changes)
    {
        try
        {
            if (Workflow.FromExportJson(draftJson) is not Workflow copy)
            {
                return null;
            }

            _ = WorkflowProposalApplier.Apply(copy, changes, new HashSet<string>(changes.Select(static change => change.Id), StringComparer.Ordinal));
            _ = copy.ToExportJson();
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return exception;
        }
    }

    private static string ApplyMessage(ProposedChange change, Exception failure)
    {
        string field = change.Field ?? change.Kind.ToString();
        return change is { Kind: ProposedChangeKind.FieldEdited, NewJson: null, Field: not null }
            ? string.Format(CultureInfo.InvariantCulture, DELETED_SETTING_ERROR, Where(change), field)
            : string.Format(CultureInfo.InvariantCulture, UNAPPLIABLE_CHANGE_ERROR, Where(change), field, failure.Message);
    }

    private static string Where(ProposedChange change)
    {
        return change.Scope switch
        {
            ProposedChangeScope.Block => $"Block \"{change.Name ?? change.Tag ?? change.TargetKey}\"",
            ProposedChangeScope.Workflow => THE_WORKFLOW,
            _ => $"{change.Scope} \"{change.Name ?? change.TargetKey}\"",
        };
    }

    private static string? BlockKeyOf(ProposedChange change)
    {
        return change.Scope == ProposedChangeScope.Block ? change.TargetKey : null;
    }

    /// <summary>The draft decompressed and parsed once for every round of the turn.</summary>
    private DraftContext ContextOf(EditorAgentDraft draft)
    {
        if (!_drafts.TryGetValue(draft.Id, out DraftContext? context))
        {
            context = new DraftContext(draft.WorkflowJson);
            _drafts[draft.Id] = context;
        }

        return context;
    }

    /// <summary>
    /// The change list, or an unreadable-file error when the file is too large, not a workflow,
    /// or proposes more changes than a person can review. <paramref name="proposedRoot"/> is the
    /// parsed proposal, null when it is unreadable.
    /// </summary>
    private WorkflowProposal Build(
        DraftContext context,
        string proposedJson,
        int size,
        List<ProposalSummaryEntry> summary,
        List<ProposalCheckError> errors,
        out JObject? proposedRoot)
    {
        proposedRoot = null;
        if (size > (long)_settings.MaxDraftMegabytes * BYTES_PER_MEGABYTE)
        {
            errors.Add(new ProposalCheckError(ProposalChecks.FILES, string.Format(
                CultureInfo.InvariantCulture, UNREADABLE_WORKFLOW, $"it is larger than {_settings.MaxDraftMegabytes} MB")));
            return new WorkflowProposal { Summary = summary };
        }

        WorkflowProposal proposal;
        JObject parsed;
        try
        {
            context.Root ??= WorkflowProposalBuilder.Parse(context.Json);
            parsed = WorkflowProposalBuilder.Parse(proposedJson);
            proposal = WorkflowProposalBuilder.Build(context.Root, parsed, summary);
        }
        catch (JsonException exception)
        {
            errors.Add(new ProposalCheckError(
                ProposalChecks.FILES, string.Format(CultureInfo.InvariantCulture, UNREADABLE_WORKFLOW, exception.Message)));
            return new WorkflowProposal { Summary = summary };
        }

        if (proposal.Changes.Count > MAX_CHANGES)
        {
            errors.Add(new ProposalCheckError(ProposalChecks.FILES, string.Format(
                CultureInfo.InvariantCulture, TOO_MANY_CHANGES, proposal.Changes.Count, MAX_CHANGES)));
            return new WorkflowProposal { Summary = summary };
        }

        proposedRoot = parsed;
        return proposal;
    }

    /// <summary>
    /// The save checks on the proposal, less what the workflow already failed before the agent
    /// touched it: those are not the agent's to fix and must not refuse its changes. Agent template
    /// tags nothing can fill only warn on a save, so they come back as warnings.
    /// </summary>
    private async Task CheckAsync(
        EditorAgentDraft draft,
        string userId,
        DraftContext context,
        string proposedJson,
        List<ProposalCheckError> errors,
        List<ProposalCheckError> warnings,
        CancellationToken cancellationToken)
    {
        Workflow? saved = await SavedTemplateAsync(draft.WorkflowTypeId);
        context.BaseFailures ??= [.. await _checks.CheckAsync(draft.WorkflowTypeId, context.Json, saved, userId, cancellationToken)];

        foreach (WorkflowCheckFailure failure in await _checks.CheckAsync(draft.WorkflowTypeId, proposedJson, saved, userId, cancellationToken))
        {
            if (context.BaseFailures.Contains(failure))
            {
                continue;
            }

            if (failure.Check == WorkflowCheck.AgentTags)
            {
                warnings.Add(ToProposalError(failure));
            }
            else
            {
                errors.Add(ToProposalError(failure));
            }
        }
    }

    private static ProposalCheckError ToProposalError(WorkflowCheckFailure failure)
    {
        string check = failure.Check switch
        {
            WorkflowCheck.Schema => ProposalChecks.SCHEMA,
            WorkflowCheck.Structure => ProposalChecks.STRUCTURE,
            WorkflowCheck.Library => ProposalChecks.LIBRARY,
            WorkflowCheck.AgentTags => ProposalChecks.AGENT_TAGS,
            WorkflowCheck.SignatureFields => ProposalChecks.SIGNATURE_FIELDS,
            _ => ProposalChecks.AGENT_GATES,
        };

        return new ProposalCheckError(check, failure.Message);
    }

    private async Task<Workflow?> SavedTemplateAsync(string workflowTypeId)
    {
        if (!_savedTemplateRead)
        {
            _savedTemplate = await _core.Workflows.GetByTypeIdAsync(workflowTypeId);
            _savedTemplateRead = true;
        }

        return _savedTemplate;
    }

    /// <summary>The workflow behind a file, for its dependency map; null when it cannot be loaded.</summary>
    private Workflow? Read(string json)
    {
        try
        {
            return Workflow.FromExportJson(json);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "A workflow file of the editor agent could not be loaded for its data flow");
            return null;
        }
    }

    private static List<ProposalCheckError> Capped(List<ProposalCheckError> errors)
    {
        return errors.Count <= MAX_ERRORS_STORED ? errors : [.. errors.Take(MAX_ERRORS_STORED)];
    }

    /// <summary>
    /// The round's work, staged for a next round that cannot continue the session: the workflow it
    /// wrote, its summary and its answer.
    /// </summary>
    private static List<AgentRunDocument> PreviousAttempt(AgentRunRecarriedFile workflowFile, AgentConversationRound round)
    {
        var documents = new List<AgentRunDocument>
        {
            new(PREVIOUS_WORKFLOW_DOCUMENT, workflowFile.Content, JSON_MEDIA_TYPE),
            new(PREVIOUS_ANSWER_DOCUMENT, Encoding.UTF8.GetBytes(round.Answer), MARKDOWN_MEDIA_TYPE),
        };

        if (round.OutFiles.FirstOrDefault(static file => file.Path == SUMMARY_FILE) is AgentRunRecarriedFile summary)
        {
            documents.Add(new AgentRunDocument(PREVIOUS_SUMMARY_DOCUMENT, summary.Content, JSON_MEDIA_TYPE));
        }

        return documents;
    }

    /// <summary>The message that sends the agent back to fix what the checks found.</summary>
    public static string GateMessage(IReadOnlyList<ProposalCheckError> errors, int round, int rounds)
    {
        var list = new StringBuilder();
        foreach (ProposalCheckError error in errors.Take(MAX_ERRORS_SHOWN))
        {
            string message = error.Message.Length > MAX_ERROR_LENGTH ? error.Message.Truncate(MAX_ERROR_LENGTH) + ELLIPSIS : error.Message;
            _ = list.AppendLine(CultureInfo.InvariantCulture, $"- [{error.Check}] {message}");
        }

        if (errors.Count > MAX_ERRORS_SHOWN)
        {
            _ = list.AppendLine(CultureInfo.InvariantCulture, $"- ... and {errors.Count - MAX_ERRORS_SHOWN} more.");
        }

        return _gateTemplate.Value
            .Replace(ROUND_SLOT, round.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace(ROUNDS_SLOT, rounds.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace(ERRORS_SLOT, list.ToString().TrimEnd(), StringComparison.Ordinal);
    }

    private static EditorAgentSummaryFile ReadSummary(IReadOnlyList<AgentRunRecarriedFile> outFiles, List<ProposalCheckError> errors)
    {
        AgentRunRecarriedFile? file = outFiles.FirstOrDefault(static file => file.Path == SUMMARY_FILE);
        if (file is null)
        {
            errors.Add(new ProposalCheckError(ProposalChecks.FILES, MISSING_SUMMARY));
            return new EditorAgentSummaryFile(null, []);
        }

        try
        {
            EditorAgentSummaryFile summary = EditorAgentSummary.Read(Encoding.UTF8.GetString(file.Content.Span));
            if (summary.Changes.Count > MAX_SUMMARY_ENTRIES)
            {
                errors.Add(new ProposalCheckError(
                    ProposalChecks.FILES, string.Format(CultureInfo.InvariantCulture, TOO_MANY_ENTRIES, MAX_SUMMARY_ENTRIES)));
                return summary with { Changes = [.. summary.Changes.Take(MAX_SUMMARY_ENTRIES)] };
            }

            return summary;
        }
        catch (JsonException exception)
        {
            errors.Add(new ProposalCheckError(
                ProposalChecks.FILES, string.Format(CultureInfo.InvariantCulture, UNREADABLE_SUMMARY, exception.Message)));
            return new EditorAgentSummaryFile(null, []);
        }
    }

    /// <summary>
    /// A resumed session carries an earlier turn's out/ files into this one: a workflow file
    /// identical to an earlier turn's proposal is that proposal, not a new one, in any round.
    /// </summary>
    private async Task<bool> CarriedOverAsync(AgentConversationTurn turn, string sha256, CancellationToken cancellationToken)
    {
        return await _userData.Repository<EditorAgentProposal>()
            .GetAll()
            .AsNoTracking()
            .AnyAsync(row => row.ConversationId == turn.ConversationId
                && row.TurnId != turn.Id
                && row.ProposedWorkflowSha256 == sha256,
                cancellationToken);
    }

    private async Task SaveAsync(
        AgentConversationTurn turn,
        EditorAgentDraft draft,
        string sha256,
        WorkflowProposal proposal,
        CancellationToken cancellationToken)
    {
        EditorAgentProposal? row = await _userData.Repository<EditorAgentProposal>()
            .GetAll()
            .SingleOrDefaultAsync(existing => existing.TurnId == turn.Id, cancellationToken);

        if (row is null)
        {
            row = new EditorAgentProposal { TurnId = turn.Id, ConversationId = turn.ConversationId };
            _userData.Repository<EditorAgentProposal>().Add(row);
        }
        else
        {
            row.UpdatedAt = DateTime.UtcNow;
        }

        row.DraftId = draft.Id;
        row.ProposedWorkflowSha256 = sha256;
        row.Proposal = proposal;
        row.Version = EditorAgentProposal.NewVersion();
        _ = await _userData.CompleteAsync(cancellationToken);
    }

    private sealed class DraftContext(string json)
    {
        public string Json { get; } = json;

        public JObject? Root { get; set; }

        public Workflow? Workflow { get; set; }

        /// <summary>
        /// The draft's own text for the report-text check: what its author wrote, never a test
        /// run's output left on it.
        /// </summary>
        public EditorAgentWorkflowText? AuthoredText { get; set; }

        /// <summary>What the draft already failed before the agent touched it.</summary>
        public HashSet<WorkflowCheckFailure>? BaseFailures { get; set; }
    }
}

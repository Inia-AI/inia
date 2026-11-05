using System.Globalization;
using System.Text;
using IniaAi.Common;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Domain.Entities.ReportFeedback;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Proposals;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.Application.Workflows.EditorAgent;

/// <summary>
/// Report feedback as the editor agent works on it. The "Use feedback" button lists the open
/// items of the open workflow's type, the caller's organization's or every organization's for a
/// super admin, and sends the ones the user chooses. The agent reads them as <see cref="DOCUMENT"/> with the runs that hold the most
/// items, names the items each change fixes in its summary, and may suggest dismissing an item in
/// <see cref="DISMISS_FILE"/>. A save of a change that names an item marks the item addressed.
/// The user may delete an item from the list for good; every read here leaves a deleted item out.
/// </summary>
public sealed class EditorAgentFeedback
{
    public const string DOCUMENT = "feedback.md";
    public const string DISMISS_FILE = "feedback.json";
    public const int MAX_SENT = 30;
    public const int MAX_LISTED = 200;
    public const int MAX_DISMISS_FILE_BYTES = 64 * 1024;

    private const string DISMISS_KEY = "dismiss";
    private const string ID_KEY = "id";
    private const string REASON_KEY = "reason";
    private const string MARKDOWN_MEDIA_TYPE = "text/markdown";
    private const string ORDER_FIELD = "Order";
    private const string TITLE = "# Open feedback on reports of {0}, newest first";
    private const string ALL_LISTED = "{0} open items.";
    private const string SOME_LISTED = "{0} open items; these are the {1} the user chose.";
    private const string STAGED = "staged whole as report-{0}.md";
    private const string NOT_STAGED = "not staged: open it with out/open-reports.json";
    private const string NOTE =
        "\n\n---\nThe user sent {0} open feedback items on this workflow's reports: read docs/feedback.md. "
        + "Name the items each change fixes in its summary entry as \"feedback\"; suggest dismissing an item the workflow cannot fix in out/feedback.json.";

    private readonly IUserDataUnitOfWork _userData;

    public EditorAgentFeedback(IUserDataUnitOfWork userData)
    {
        _userData = userData;
    }

    public Task<int> CountOpenAsync(
        string organizationId,
        bool acrossOrganizations,
        string workflowTypeId,
        CancellationToken cancellationToken)
    {
        return Open(organizationId, acrossOrganizations, workflowTypeId).CountAsync(cancellationToken);
    }

    /// <summary>The newest open items, at most <see cref="MAX_LISTED"/>, for the user to choose from.</summary>
    public Task<List<ReportFeedbackItem>> NewestOpenAsync(
        string organizationId,
        bool acrossOrganizations,
        string workflowTypeId,
        CancellationToken cancellationToken)
    {
        return Newest(Open(organizationId, acrossOrganizations, workflowTypeId))
            .Take(MAX_LISTED)
            .ToListAsync(cancellationToken);
    }

    /// <summary>The chosen items that are still open, newest first; one closed or purged since is left out.</summary>
    public Task<List<ReportFeedbackItem>> ChosenOpenAsync(
        string organizationId,
        bool acrossOrganizations,
        string workflowTypeId,
        IReadOnlyCollection<string> ids,
        CancellationToken cancellationToken)
    {
        return Newest(Open(organizationId, acrossOrganizations, workflowTypeId).Where(item => ids.Contains(item.Id)))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Deletes the item for good, whatever its status, when it is in the scope the list reads
    /// from; false when it is not.
    /// </summary>
    public async Task<bool> DeleteAsync(
        string organizationId,
        bool acrossOrganizations,
        string workflowTypeId,
        string id,
        CancellationToken cancellationToken)
    {
        ReportFeedbackItem? item = await Visible(organizationId, acrossOrganizations, workflowTypeId)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (item is null)
        {
            return false;
        }

        _userData.Repository<ReportFeedbackItem>().HardRemove(item);
        _ = await _userData.CompleteAsync(cancellationToken);
        return true;
    }

    /// <summary>The items with these ids, in the order given; one purged since is left out.</summary>
    public async Task<List<ReportFeedbackItem>> ByIdsAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        Dictionary<string, ReportFeedbackItem> found = await _userData.Repository<ReportFeedbackItem>()
            .GetAll()
            .AsNoTracking()
            .Where(item => ids.Contains(item.Id))
            .ToDictionaryAsync(static item => item.Id, StringComparer.Ordinal, cancellationToken);

        return [.. ids.Distinct(StringComparer.Ordinal).Select(id => found.GetValueOrDefault(id)).OfType<ReportFeedbackItem>()];
    }

    /// <summary>The feedback rows of the conversation's turns, by turn id.</summary>
    public Task<Dictionary<string, EditorAgentTurnFeedback>> OfConversationAsync(string conversationId, CancellationToken cancellationToken)
    {
        return _userData.Repository<EditorAgentTurnFeedback>()
            .GetAll()
            .AsNoTracking()
            .Where(row => row.ConversationId == conversationId)
            .ToDictionaryAsync(static row => row.TurnId, StringComparer.Ordinal, cancellationToken);
    }

    /// <summary>
    /// Every item the conversation was sent, which its turns may cite and its report-text check
    /// counts as report text.
    /// </summary>
    public async Task<List<ReportFeedbackItem>> SentInConversationAsync(string conversationId, CancellationToken cancellationToken)
    {
        Dictionary<string, EditorAgentTurnFeedback> rows = await OfConversationAsync(conversationId, cancellationToken);
        return await ByIdsAsync([.. rows.Values.SelectMany(static row => row.Sent).Distinct(StringComparer.Ordinal)], cancellationToken);
    }

    /// <summary>
    /// The runs to stage whole for the items, at most <paramref name="max"/>: the runs with the
    /// most items first, then the one with the newest item.
    /// </summary>
    public static IReadOnlyList<EditorAgentFeedbackRun> RunsToStage(IEnumerable<ReportFeedbackItem> items, int max)
    {
        return [.. items
            .GroupBy(static item => item.WorkflowExecutionId, StringComparer.Ordinal)
            .OrderByDescending(static run => run.Count())
            .ThenByDescending(static run => run.Max(static item => item.CreatedAt))
            .Take(Math.Max(0, max))
            .Select(static run => new EditorAgentFeedbackRun(run.Key, run.First().OrganizationId))];
    }

    /// <summary>The run each item is on, with its organization, for staging runs of other organizations.</summary>
    public static Dictionary<string, string> RunOrganizations(IEnumerable<ReportFeedbackItem> items)
    {
        var runs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ReportFeedbackItem item in items)
        {
            runs[item.WorkflowExecutionId] = item.OrganizationId;
        }

        return runs;
    }

    /// <summary>The text of the items, which no change may copy into the workflow.</summary>
    public static IEnumerable<string> Texts(IEnumerable<ReportFeedbackItem> items)
    {
        return items.SelectMany(static item => new[] { item.Comment, item.QuotedText }).OfType<string>();
    }

    public static AgentRunDocument Document(
        string workflowTypeName,
        IReadOnlyList<ReportFeedbackItem> items,
        int openCount,
        IReadOnlySet<string> stagedRunIds)
    {
        return new AgentRunDocument(DOCUMENT, Encoding.UTF8.GetBytes(Markdown(workflowTypeName, items, openCount, stagedRunIds)), MARKDOWN_MEDIA_TYPE);
    }

    public static string Markdown(
        string workflowTypeName,
        IReadOnlyList<ReportFeedbackItem> items,
        int openCount,
        IReadOnlySet<string> stagedRunIds)
    {
        var builder = new StringBuilder();
        _ = builder.AppendLine(string.Format(CultureInfo.InvariantCulture, TITLE, workflowTypeName)).AppendLine();
        _ = builder.AppendLine(openCount > items.Count
            ? string.Format(CultureInfo.InvariantCulture, SOME_LISTED, openCount, items.Count)
            : string.Format(CultureInfo.InvariantCulture, ALL_LISTED, items.Count));

        foreach (ReportFeedbackItem item in items)
        {
            string staged = stagedRunIds.Contains(item.WorkflowExecutionId)
                ? string.Format(CultureInfo.InvariantCulture, STAGED, item.WorkflowExecutionId)
                : NOT_STAGED;
            _ = builder.AppendLine()
                .AppendLine(CultureInfo.InvariantCulture, $"## {item.Id}")
                .AppendLine(CultureInfo.InvariantCulture, $"- Report: {item.WorkflowExecutionId} ({staged}), {item.CreatedAt:yyyy-MM-dd}")
                .AppendLine(CultureInfo.InvariantCulture, $"- Category: {CategoryName(item.Category)}")
                .AppendLine(CultureInfo.InvariantCulture, $"- Comment: {OneLine(item.Comment)}");
            if (!string.IsNullOrWhiteSpace(item.QuotedText))
            {
                _ = builder.AppendLine(CultureInfo.InvariantCulture, $"- Quoted: \"{OneLine(item.QuotedText)}\"");
            }
        }

        return builder.ToString().TrimEnd() + "\n";
    }

    /// <summary>The platform's note after the message, naming the feedback it was sent with.</summary>
    public static string Note(int sent)
    {
        return string.Format(CultureInfo.InvariantCulture, NOTE, sent);
    }

    public static string CategoryName(ReportFeedbackCategory category)
    {
        return category switch
        {
            ReportFeedbackCategory.WrongFigure => "Wrong figure",
            ReportFeedbackCategory.MissingInformation => "Missing information",
            ReportFeedbackCategory.WrongClassification => "Wrong classification",
            ReportFeedbackCategory.WordingOrStyle => "Wording or style",
            ReportFeedbackCategory.NotRelevant => "Not relevant",
            _ => category.ToString(),
        };
    }

    /// <summary>
    /// The agent's dismiss suggestions from <see cref="DISMISS_FILE"/>, kept only for items in
    /// <paramref name="sentIds"/>, each once; null when the file is unreadable.
    /// </summary>
    public static List<EditorAgentDismissSuggestion>? ReadDismissals(ReadOnlySpan<byte> content, IReadOnlySet<string> sentIds)
    {
        if (content.Length > MAX_DISMISS_FILE_BYTES)
        {
            return null;
        }

        JToken root;
        try
        {
            root = JToken.Parse(Encoding.UTF8.GetString(content));
        }
        catch (JsonException)
        {
            return null;
        }

        if ((root is JObject request ? request[DISMISS_KEY] : root) is not JArray entries)
        {
            return null;
        }

        var suggestions = new List<EditorAgentDismissSuggestion>();
        foreach (JObject entry in entries.OfType<JObject>())
        {
            string? id = entry[ID_KEY]?.Type == JTokenType.String ? ((string?)entry[ID_KEY])?.Trim() : null;
            string? reason = entry[REASON_KEY]?.Type == JTokenType.String ? ((string?)entry[REASON_KEY])?.Trim() : null;
            if (id is null || string.IsNullOrEmpty(reason) || !sentIds.Contains(id) || suggestions.Any(suggestion => suggestion.FeedbackItemId == id))
            {
                continue;
            }

            suggestions.Add(new EditorAgentDismissSuggestion(id, reason.Truncate(ReportFeedbackItem.MAX_DISMISS_REASON_LENGTH)!));
        }

        return suggestions;
    }

    public static string DismissalsJson(IReadOnlyList<EditorAgentDismissSuggestion> suggestions)
    {
        return JsonConvert.SerializeObject(suggestions);
    }

    public static IReadOnlyList<EditorAgentDismissSuggestion> Dismissals(string? json)
    {
        return string.IsNullOrWhiteSpace(json)
            ? []
            : JsonConvert.DeserializeObject<List<EditorAgentDismissSuggestion>>(json) ?? [];
    }

    /// <summary>
    /// The feedback items the saved changes fix: those named by a summary entry that covers a
    /// saved change. An entry covers a block's change by the block's id or tag, or by its section;
    /// a section's change by the section's id or title. A change of order alone fixes nothing.
    /// </summary>
    public static HashSet<string> Cited(WorkflowProposal proposal, IReadOnlySet<string> savedChangeIds)
    {
        var cited = new HashSet<string>(StringComparer.Ordinal);
        List<ProposalSummaryEntry> citing = [.. proposal.Summary.Where(static entry => entry.FeedbackIds is { Count: > 0 })];
        if (citing.Count == 0)
        {
            return cited;
        }

        foreach (ProposedChange change in proposal.Changes.Where(change => savedChangeIds.Contains(change.Id) && change.Field != ORDER_FIELD))
        {
            foreach (ProposalSummaryEntry entry in citing.Where(entry => Covers(entry, change)))
            {
                cited.UnionWith(entry.FeedbackIds!);
            }
        }

        return cited;
    }

    private static bool Covers(ProposalSummaryEntry entry, ProposedChange change)
    {
        return change.Scope switch
        {
            ProposedChangeScope.Block => (entry.StableBlockId is string block && (block == change.TargetKey || block == change.Tag))
                || (entry.StableBlockId is null && entry.Section is string section && section == change.SectionKey),
            ProposedChangeScope.Section => entry.Section is string title
                && (title == change.TargetKey || string.Equals(title, change.Name, StringComparison.OrdinalIgnoreCase)),
            _ => false,
        };
    }

    private IQueryable<ReportFeedbackItem> Open(string organizationId, bool acrossOrganizations, string workflowTypeId)
    {
        return Visible(organizationId, acrossOrganizations, workflowTypeId)
            .AsNoTracking()
            .Where(item => item.Status == ReportFeedbackStatus.Open);
    }

    /// <summary>
    /// The items on live reports of the workflow type, of the caller's organization or of every
    /// organization: the scope the list reads from and a delete may reach.
    /// </summary>
    private IQueryable<ReportFeedbackItem> Visible(string organizationId, bool acrossOrganizations, string workflowTypeId)
    {
        IQueryable<WorkflowExecution> runs = _userData.Repository<WorkflowExecution>().GetAll();
        return _userData.Repository<ReportFeedbackItem>()
            .GetAll()
            .Where(item => item.WorkflowTypeId == workflowTypeId
                && (acrossOrganizations || item.OrganizationId == organizationId)
                && runs.Any(run => run.Id == item.WorkflowExecutionId && !run.IsDeleted));
    }

    private static IQueryable<ReportFeedbackItem> Newest(IQueryable<ReportFeedbackItem> items)
    {
        return items.OrderByDescending(static item => item.CreatedAt).ThenBy(static item => item.Id);
    }

    private static string OneLine(string text)
    {
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}

/// <summary>A run that holds feedback items, with the organization it belongs to.</summary>
public sealed record EditorAgentFeedbackRun(string ExecutionId, string OrganizationId);

/// <param name="Reason">Why the agent thinks the workflow cannot fix the item, one plain sentence.</param>
public sealed record EditorAgentDismissSuggestion(string FeedbackItemId, string Reason);

/// <summary>An item the agent suggests dismissing, as the panel shows it with its Dismiss button.</summary>
/// <param name="Status">The item's status now: Open until someone dismisses or addresses it.</param>
public sealed record EditorAgentDismissSuggestionView(
    string FeedbackItemId,
    string Reason,
    ReportFeedbackCategory Category,
    string Comment,
    string? QuotedText,
    ReportFeedbackStatus Status);

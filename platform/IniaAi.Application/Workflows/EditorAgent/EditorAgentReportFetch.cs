using System.Globalization;
using System.Text;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.Application.Workflows.EditorAgent;

/// <summary>
/// One turn's report requests. When a round ends with a new <c>out/open-reports.json</c>, the
/// reports it names that are the organization's runs of the open workflow's type are staged and the
/// agent goes on within the same turn, at most <see cref="MAX_FETCH_ROUNDS"/> times. Reports
/// fetched in earlier rounds stay staged for every later round of the turn. Each fetched report
/// joins the turn's report-text check at once, and is recorded with the turn so every later turn
/// of the conversation checks against it too.
/// </summary>
public sealed class EditorAgentReportFetch
{
    public const string OPEN_REPORTS_FILE = "open-reports.json";
    public const int MAX_FETCH_ROUNDS = 2;
    public const int MAX_REQUEST_BYTES = 4096;

    private const string IDS_KEY = "ids";
    private const string STAGED_LINE = "The platform staged {0} for you to read, each with its run file and its documents.";
    private const string UNKNOWN_LINE = "{0} of the ids are not reports of this workflow in your organization; use ids from reports.md.";
    private const string ALREADY_LINE = "Already staged: {0}.";
    private const string OVER_LIMIT_LINE = "Only the first {0} ids were read; ask for the rest in a later round if you still need them.";
    private const string UNREADABLE_LINE =
        "out/open-reports.json could not be read. Write it as {\"ids\": [\"<report id>\"]} with at most 3 ids from reports.md.";
    private const string CLOSING_LINE =
        "Delete out/open-reports.json and go on with your answer.";

    private readonly EditorAgentReports _reports;
    private readonly EditorAgentReportDocuments _documents;
    private readonly EditorAgentReportBudget _budget;
    private readonly IUserDataUnitOfWork _userData;
    private readonly AgentConversationTurn _turn;
    private readonly string _organizationId;
    private readonly string _workflowTypeId;
    private readonly EditorAgentReportLeaks _leaks;
    private readonly HashSet<string> _staged;
    private readonly IReadOnlyDictionary<string, string> _feedbackRuns;
    private readonly List<EditorAgentReportEvidence> _fetched = [];
    private string? _lastRequestSha256;

    /// <param name="budget">What the message may still stage of report documents, shared with the user's picks.</param>
    /// <param name="leaks">The turn's report-text check, which every fetched report joins.</param>
    /// <param name="staged">The reports the turn already stages: the user's picks and the runs of the feedback sent.</param>
    /// <param name="feedbackRuns">
    /// The runs of the feedback items the conversation was sent, with their organizations: the
    /// agent may open these too, also when another organization's, which only a super admin sends.
    /// </param>
    public EditorAgentReportFetch(
        EditorAgentReports reports,
        EditorAgentReportDocuments documents,
        EditorAgentReportBudget budget,
        IUserDataUnitOfWork userData,
        AgentConversationTurn turn,
        string organizationId,
        string workflowTypeId,
        EditorAgentReportLeaks leaks,
        IEnumerable<string> staged,
        IReadOnlyDictionary<string, string>? feedbackRuns = null)
    {
        _leaks = leaks;
        _reports = reports;
        _documents = documents;
        _budget = budget;
        _userData = userData;
        _turn = turn;
        _organizationId = organizationId;
        _workflowTypeId = workflowTypeId;
        _staged = new HashSet<string>(staged, StringComparer.Ordinal);
        _feedbackRuns = feedbackRuns ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>The reports fetched so far this turn.</summary>
    public IReadOnlyList<EditorAgentReportEvidence> Fetched => _fetched;

    private IEnumerable<AgentRunDocument> FetchedDocuments => _fetched.SelectMany(static evidence => evidence.Documents);

    /// <summary>The fetch rounds the turn has run.</summary>
    public int Rounds { get; private set; }

    /// <summary>
    /// The round's report request answered, or null when the round made none to answer: no
    /// request file, the one already answered or carried over from an earlier turn, no fetch
    /// round left, or no round left at all.
    /// </summary>
    public async Task<AgentConversationRoundReview?> TryFetchAsync(
        AgentConversationRound round,
        CancellationToken cancellationToken)
    {
        AgentRunRecarriedFile? file = round.OutFiles.FirstOrDefault(static file => file.Path == OPEN_REPORTS_FILE);
        if (file is null || round.IsLastRound || Rounds >= MAX_FETCH_ROUNDS || file.Sha256 == _lastRequestSha256)
        {
            return null;
        }

        if (await CarriedOverAsync(file.Sha256, cancellationToken))
        {
            return null;
        }

        _lastRequestSha256 = file.Sha256;
        await RecordRequestAsync(file.Sha256, cancellationToken);
        Rounds++;

        List<string>? requested = ReadIds(file);
        if (requested is null)
        {
            return Continue(UNREADABLE_LINE);
        }

        var message = new StringBuilder();
        List<string> distinct = [.. requested.Distinct(StringComparer.OrdinalIgnoreCase)];
        int invalid = distinct.Count(static id => !Guid.TryParseExact(id, "D", out _));
        List<string> asked = [.. distinct.Where(static id => Guid.TryParseExact(id, "D", out _))];
        if (asked.Count > EditorAgentReports.MAX_OPENED_PER_ROUND)
        {
            _ = message.AppendLine(string.Format(CultureInfo.InvariantCulture, OVER_LIMIT_LINE, EditorAgentReports.MAX_OPENED_PER_ROUND));
            asked = [.. asked.Take(EditorAgentReports.MAX_OPENED_PER_ROUND)];
        }

        List<string> already = [.. asked.Where(_staged.Contains)];
        List<string> wanted = [.. asked.Where(id => !_staged.Contains(id))];
        IReadOnlyList<string> owned = await _reports.OwnedIdsAsync(_organizationId, [_workflowTypeId], wanted, cancellationToken);
        IEnumerable<(string Id, string Organization)> openable = wanted
            .Select(id => (Id: id, Organization: owned.Contains(id) ? _organizationId : _feedbackRuns.GetValueOrDefault(id)))
            .Where(static run => run.Organization is not null)
            .Select(static run => (run.Id, run.Organization!));

        var staged = new List<string>();
        foreach ((string id, string organizationId) in openable)
        {
            EditorAgentReportEvidence? evidence = await _reports.EvidenceAsync(organizationId, [_workflowTypeId], id, cancellationToken, _budget, new EditorAgentOpenWorkflowType(_workflowTypeId, new HashSet<string>()));
            if (evidence is not null)
            {
                EditorAgentReportEvidence withDocuments = await _documents.AddAsync(organizationId, evidence, _budget, cancellationToken);
                _fetched.Add(withDocuments);
                _leaks.Add(withDocuments);
                _ = _staged.Add(id);
                staged.Add(id);
            }
        }

        if (staged.Count > 0)
        {
            await RecordFetchedAsync(staged, cancellationToken);
        }

        int unknown = invalid + wanted.Count(id => !staged.Contains(id));
        if (staged.Count > 0)
        {
            _ = message.AppendLine(string.Format(
                CultureInfo.InvariantCulture, STAGED_LINE, string.Join(", ", staged.Select(EditorAgentReports.HeaderDocumentName))));
        }

        if (unknown > 0)
        {
            _ = message.AppendLine(string.Format(CultureInfo.InvariantCulture, UNKNOWN_LINE, unknown));
        }

        if (already.Count > 0)
        {
            _ = message.AppendLine(string.Format(
                CultureInfo.InvariantCulture, ALREADY_LINE, string.Join(", ", already.Select(EditorAgentReports.HeaderDocumentName))));
        }

        return Continue(message.ToString().TrimEnd());
    }

    /// <summary>A review of the stop gate, with the fetched reports staged beside its own documents.</summary>
    public AgentConversationRoundReview WithFetched(AgentConversationRoundReview review)
    {
        if (review.ContinueWith is null || _fetched.Count == 0)
        {
            return review;
        }

        return review with { Documents = [.. review.Documents, .. FetchedDocuments] };
    }

    private AgentConversationRoundReview Continue(string message)
    {
        string text = string.IsNullOrWhiteSpace(message) ? CLOSING_LINE : message + "\n" + CLOSING_LINE;
        return AgentConversationRoundReview.Continue(text, [.. FetchedDocuments]);
    }

    private static List<string>? ReadIds(AgentRunRecarriedFile file)
    {
        if (file.Content.Length > MAX_REQUEST_BYTES)
        {
            return null;
        }

        try
        {
            JToken root = JToken.Parse(Encoding.UTF8.GetString(file.Content.Span));
            JToken? ids = root is JObject request ? request[IDS_KEY] : root;
            return ids is JArray array
                ? [.. array.Select(static item => item.Type == JTokenType.String ? item.Value<string>() : null)
                    .OfType<string>()
                    .Select(static id => id.Trim())
                    .Where(static id => id.Length > 0)]
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// A resumed session carries an earlier turn's out/ files into this one: a request identical
    /// to one an earlier turn answered is that request, not a new one.
    /// </summary>
    private Task<bool> CarriedOverAsync(string sha256, CancellationToken cancellationToken)
    {
        return _userData.Repository<EditorAgentTurnReports>()
            .GetAll()
            .AsNoTracking()
            .AnyAsync(row => row.ConversationId == _turn.ConversationId
                && row.TurnId != _turn.Id
                && row.OpenRequestSha256 == sha256,
                cancellationToken);
    }

    private async Task RecordRequestAsync(string sha256, CancellationToken cancellationToken)
    {
        EditorAgentTurnReports row = await RowAsync(cancellationToken);
        row.OpenRequestSha256 = sha256;
        _ = await _userData.CompleteAsync(cancellationToken);
    }

    private async Task RecordFetchedAsync(IEnumerable<string> fetched, CancellationToken cancellationToken)
    {
        EditorAgentTurnReports row = await RowAsync(cancellationToken);
        row.FetchedExecutionIds = string.Join(',', row.Fetched.Concat(fetched).Distinct(StringComparer.Ordinal));
        _ = await _userData.CompleteAsync(cancellationToken);
    }

    private async Task<EditorAgentTurnReports> RowAsync(CancellationToken cancellationToken)
    {
        EditorAgentTurnReports? row = await _userData.Repository<EditorAgentTurnReports>()
            .GetAll()
            .SingleOrDefaultAsync(existing => existing.TurnId == _turn.Id, cancellationToken);
        if (row is null)
        {
            row = new EditorAgentTurnReports { TurnId = _turn.Id, ConversationId = _turn.ConversationId };
            _userData.Repository<EditorAgentTurnReports>().Add(row);
        }

        return row;
    }
}

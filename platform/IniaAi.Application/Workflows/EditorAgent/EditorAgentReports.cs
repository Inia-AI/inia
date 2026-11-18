using System.Globalization;
using System.Text;
using IniaAi.Application.Common.Workflows;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Common.Serialization;
using IniaAi.Configuration.Platform;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.Domain.Proposals;
using IniaAi.Domain.Serialization;
using IniaAi.Domain.Specifications.Workflows;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace IniaAi.Application.Workflows.EditorAgent;

/// <summary>
/// The user's past reports, as the editor agent reads them: a list of the organization's recent
/// runs of the open workflow's type, and one run staged whole, as its executed workflow. Every
/// read is bounded to one organization and to the workflow types the caller names, so a report id
/// of another organization or of a type outside them reads as unknown. The user may pick reports
/// of any type their organization has access to; the agent opens only reports of the open type.
/// A run is the organization's own data and can quote its documents.
/// </summary>
public sealed class EditorAgentReports
{
    public const string LIST_DOCUMENT = "reports.md";
    public const int LIST_SIZE = 30;
    public const int MAX_PAGE_SIZE = 50;
    public const int MAX_PICKED = 5;
    public const int MAX_OPENED_PER_ROUND = 3;
    public const string COMPLETED = "completed";
    public const string CONFIRMED = "confirmed";
    public const string FAILED = "failed";
    public const string CANCELLED = "cancelled";
    public const string GZIP_SUFFIX = ".gz";
    public const string TEMPLATE_STABLE_ID_KEY = "TemplateStableBlockId";
    public const string TEMPLATE_TAG_KEY = "TemplateTag";
    public const string VARIANT_KEY = "Variant";

    /// <summary>A run file past this size is sent gzipped.</summary>
    public const long COMPRESS_ABOVE_BYTES = 4L * BYTES_PER_MEGABYTE;

    private const int BYTES_PER_MEGABYTE = 1024 * 1024;
    private const string REPORT_PREFIX = "report-";
    private const string OPEN_TYPE_MARK = ", the open workflow: this report is a run of it, so its blocks are this workflow's blocks, possibly as an earlier version had them";
    private const string OTHER_TYPE_MARK = ", not the open workflow: its blocks are that workflow's, not this one's";
    private const string COPY_TYPE_MARK = ", copied from or into the open workflow, directly or through other copies: its block ids match this workflow's where the blocks still exist; its blocks and prompts may differ";
    private const int MAX_LINEAGE_DEPTH = 64;
    private const string HEADER_SUFFIX = ".md";
    private const string RUN_SUFFIX = ".workflow.json";
    private const string JSON_MEDIA_TYPE = "application/json";
    private const string GZIP_MEDIA_TYPE = "application/gzip";
    private const string MARKDOWN_MEDIA_TYPE = "text/markdown";
    private const string SECTIONS_KEY = "Sections";
    private const string BLOCKS_KEY = "Blocks";
    private const string STABLE_ID_KEY = "StableBlockId";
    private const string TAG_KEY = "ReplacementTag";
    private const string VARIANT_SOURCE_KEY = "ExpandedFromParameterizedKey";
    private const string LIST_TITLE = "# Past reports of {0}, newest first";
    private const string LIST_INTRO =
        "Your organization's recent runs of this workflow. To read one, write out/open-reports.json "
        + "as {\"ids\": [\"<id>\"]} (at most 3 at a time); it arrives as report-<id>.md and report-<id>.workflow.json.";
    private const string NO_REPORTS_LINE = "This workflow has no past reports in your organization yet.";
    private const string NO_COUNTERPARTY = "no counterparty";
    private const string NO_DOCUMENTS = "no documents";
    private const string RUN_EXPLAINED =
        "Every block carries its output (`BlockResponse`), its status and error (`ExecutionState`) and the prompt as sent "
        + "(`Properties.ExecutionState`). A block copied per period, variant or loop item carries `TemplateStableBlockId`, "
        + "the StableBlockId of the block it was copied from, and a variant copy its `Variant` and `TemplateTag`.";
    private const string RUN_LINE = "The run is `{0}`: the executed workflow, {1} blocks, {2}. " + RUN_EXPLAINED;
    private const string COMPRESSED_RUN_LINE =
        "The run is `{0}`, gzipped: the executed workflow, {1} blocks, {2} unpacked. wf.py reads it as it is; "
        + "`gzip -dc` unpacks it. " + RUN_EXPLAINED;
    private const string TOO_LARGE_LINE =
        "The run's executed workflow is {0}, over the {1} limit, so it is not staged: only this header is.";
    private const string OVER_BUDGET_LINE = "The run's executed workflow is {0}, but {1}, so it is not staged: only this header is.";
    private const string NO_RUN_LINE = "The run's executed workflow was not found, so only this header is staged.";
    private const int MAX_DOCUMENT_NAMES = 12;

    private static readonly WorkflowState[] _listedStates =
    [
        WorkflowState.Draft,
        WorkflowState.Confirmed,
        WorkflowState.Failed,
        WorkflowState.Cancelled,
    ];

    private readonly IUserDataUnitOfWork _userData;
    private readonly ICoreUnitOfWork _core;
    private readonly WorkflowTypeAccess _access;
    private readonly EditorAgentSettings _settings;

    public EditorAgentReports(IUserDataUnitOfWork userData, ICoreUnitOfWork core, WorkflowTypeAccess access, IOptions<EditorAgentSettings> settings)
    {
        _userData = userData;
        _core = core;
        _access = access;
        _settings = settings.Value;
    }

    /// <summary>The header the agent reads first: <c>report-&lt;id&gt;.md</c>.</summary>
    public static string HeaderDocumentName(string executionId)
    {
        return REPORT_PREFIX + executionId + HEADER_SUFFIX;
    }

    /// <summary>The executed workflow: <c>report-&lt;id&gt;.workflow.json</c>, with <see cref="GZIP_SUFFIX"/> when compressed.</summary>
    public static string RunDocumentName(string executionId)
    {
        return REPORT_PREFIX + executionId + RUN_SUFFIX;
    }

    /// <summary>The organization's latest runs of the type, newest first, at most <see cref="LIST_SIZE"/>.</summary>
    public async Task<IReadOnlyList<EditorAgentReportSummary>> ListAsync(
        string organizationId,
        string workflowTypeId,
        CancellationToken cancellationToken)
    {
        EditorAgentReportPage page = await PageAsync(organizationId, workflowTypeId, new EditorAgentReportQuery(), cancellationToken);
        return page.Reports;
    }

    /// <summary>
    /// One page of the organization's runs of the type that match the query, newest first. The
    /// search matches the counterparty, the report title and the document names.
    /// </summary>
    public async Task<EditorAgentReportPage> PageAsync(
        string organizationId,
        string workflowTypeId,
        EditorAgentReportQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<WorkflowExecution> runs = Owned(organizationId, [workflowTypeId]);

        if (query.Status is string status && StateOf(status) is WorkflowState state)
        {
            runs = runs.Where(execution => execution.WorkflowState == state);
        }

        if (query.From is DateTime from)
        {
            DateTime start = DateTime.SpecifyKind(from.Date, DateTimeKind.Utc);
            runs = runs.Where(execution => (execution.CompletedAt ?? execution.CreatedAt) >= start);
        }

        if (query.To is DateTime to)
        {
            DateTime end = DateTime.SpecifyKind(to.Date.AddDays(1), DateTimeKind.Utc);
            runs = runs.Where(execution => (execution.CompletedAt ?? execution.CreatedAt) < end);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string search = query.Search.Trim().ToLowerInvariant();
            IQueryable<string> matchingJobs = _userData.Repository<DocumentInfo>()
                .GetAll()
                .Where(info => info.JobId != null
                    && info.ExcludedAt == null
                    && info.Document != null
                    && info.Document.OrganizationId == organizationId
                    && (info.Document.Name.ToLower().Contains(search)
                        || (info.Document.FileMetadata != null && info.Document.FileMetadata.Name.ToLower().Contains(search))))
                .Select(static info => info.JobId!);
            runs = runs.Where(execution => execution.Title.ToLower().Contains(search)
                || (execution.Counterparty != null && execution.Counterparty.Name.ToLower().Contains(search))
                || (execution.JobId != null && matchingJobs.Contains(execution.JobId)));
        }

        int take = Math.Clamp(query.Take, 1, MAX_PAGE_SIZE);
        var rows = await runs
            .OrderByDescending(static execution => execution.CreatedAt)
            .ThenByDescending(static execution => execution.Id)
            .Skip(Math.Max(0, query.Skip))
            .Take(take + 1)
            .Select(static execution => new SummaryRow(
                execution.Id,
                execution.WorkflowTypeId ?? string.Empty,
                execution.Title,
                execution.JobId,
                execution.WorkflowState,
                execution.CreatedAt,
                execution.CompletedAt,
                execution.Counterparty == null ? null : execution.Counterparty.Name))
            .ToListAsync(cancellationToken);

        bool hasMore = rows.Count > take;
        List<EditorAgentReportSummary> reports = await SummariesAsync(organizationId, [.. rows.Take(take)], cancellationToken);
        return new EditorAgentReportPage(reports, hasMore);
    }

    /// <summary>
    /// The workflow types whose reports the organization may use as examples beside the open
    /// one: the open type first, then every type the organization reaches, by name.
    /// </summary>
    public async Task<IReadOnlyList<EditorAgentWorkflowTypeOption>> AccessibleTypesAsync(
        string organizationId,
        string openWorkflowTypeId,
        CancellationToken cancellationToken)
    {
        List<string> reachable = [.. await _access.TypeIdsAsync(organizationId, cancellationToken), openWorkflowTypeId];

        List<EditorAgentWorkflowTypeOption> types = await _core.Repository<WorkflowType>()
            .GetAll()
            .AsNoTracking()
            .Where(type => reachable.Contains(type.Id))
            .Where(WorkflowType.UserFacing)
            .Select(static type => new EditorAgentWorkflowTypeOption(type.Id, type.Name))
            .ToListAsync(cancellationToken);

        return [.. types
            .OrderByDescending(type => type.Id == openWorkflowTypeId)
            .ThenBy(static type => type.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// The ids that name a listed run of one of the types in the organization, in the order
    /// given, each once; the rest are dropped.
    /// </summary>
    public async Task<IReadOnlyList<string>> OwnedIdsAsync(
        string organizationId,
        IReadOnlyCollection<string> workflowTypeIds,
        IReadOnlyCollection<string> executionIds,
        CancellationToken cancellationToken)
    {
        List<string> wanted = [.. executionIds.Where(static id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal)];
        if (wanted.Count == 0)
        {
            return [];
        }

        HashSet<string> owned = [.. await Owned(organizationId, workflowTypeIds)
            .Where(execution => wanted.Contains(execution.Id))
            .Select(static execution => execution.Id)
            .ToListAsync(cancellationToken)];

        return [.. wanted.Where(owned.Contains)];
    }

    /// <summary>
    /// The organization's reports with these ids, as the conversation names the reports a message
    /// was sent with. A report deleted since is left out.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, EditorAgentReportSummary>> SummariesByIdAsync(
        string organizationId,
        IReadOnlyCollection<string> executionIds,
        CancellationToken cancellationToken)
    {
        List<string> wanted = [.. executionIds.Distinct(StringComparer.Ordinal)];
        if (wanted.Count == 0)
        {
            return new Dictionary<string, EditorAgentReportSummary>(StringComparer.Ordinal);
        }

        List<SummaryRow> rows = await _userData.Repository<WorkflowExecution>()
            .GetAll()
            .AsNoTracking()
            .Where(execution => execution.OrganizationId == organizationId
                && wanted.Contains(execution.Id)
                && !execution.IsDeleted)
            .Select(static execution => new SummaryRow(
                execution.Id,
                execution.WorkflowTypeId ?? string.Empty,
                execution.Title,
                execution.JobId,
                execution.WorkflowState,
                execution.CreatedAt,
                execution.CompletedAt,
                execution.Counterparty == null ? null : execution.Counterparty.Name))
            .ToListAsync(cancellationToken);

        List<EditorAgentReportSummary> summaries = await SummariesAsync(organizationId, rows, cancellationToken);
        return summaries.ToDictionary(static summary => summary.Id, StringComparer.Ordinal);
    }

    /// <summary>
    /// One run as the agent reads it, or null when the id is not a listed run of one of the types
    /// in the organization: a short header and the executed workflow itself. A run whose stored
    /// outputs alone are over the size limit is not loaded at all.
    /// </summary>
    /// <param name="budget">What the message may still stage; null stages the run whatever its size within the limit.</param>
    /// <param name="openWorkflowType">The open workflow's type, so the header says whether the run is of it; null says neither.</param>
    public Task<EditorAgentReportEvidence?> EvidenceAsync(
        string organizationId,
        IReadOnlyCollection<string> workflowTypeIds,
        string executionId,
        CancellationToken cancellationToken,
        EditorAgentReportBudget? budget = null,
        EditorAgentOpenWorkflowType? openWorkflowType = null)
    {
        return EvidenceOfAsync(organizationId, Owned(organizationId, workflowTypeIds), executionId, budget, cancellationToken, openWorkflowType);
    }

    /// <summary>
    /// A report an earlier turn of the conversation staged, read again only for the report-text
    /// check: bounded to the organization, but not to the types it may use today or to runs not
    /// deleted since, because the agent read it whatever happened after.
    /// </summary>
    public Task<EditorAgentReportEvidence?> StagedEarlierAsync(
        string organizationId,
        string executionId,
        CancellationToken cancellationToken)
    {
        IQueryable<WorkflowExecution> runs = _userData.Repository<WorkflowExecution>()
            .GetAll()
            .AsNoTracking()
            .Where(execution => execution.OrganizationId == organizationId);
        return EvidenceOfAsync(organizationId, runs, executionId, budget: null, cancellationToken);
    }

    private async Task<EditorAgentReportEvidence?> EvidenceOfAsync(
        string organizationId,
        IQueryable<WorkflowExecution> runs,
        string executionId,
        EditorAgentReportBudget? budget,
        CancellationToken cancellationToken,
        EditorAgentOpenWorkflowType? openWorkflowType = null)
    {
        var execution = await runs
            .Where(row => row.Id == executionId)
            .Select(static row => new
            {
                Row = new SummaryRow(
                    row.Id,
                    row.WorkflowTypeId ?? string.Empty,
                    row.Title,
                    row.JobId,
                    row.WorkflowState,
                    row.CreatedAt,
                    row.CompletedAt,
                    row.Counterparty == null ? null : row.Counterparty.Name),
                row.WorkflowId,
                row.FailureKind,
                row.FailureDetail,
                row.Variables,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (execution is null)
        {
            return null;
        }

        string workflowTypeId = execution.Row.WorkflowTypeId;
        string workflowId = execution.WorkflowId ?? executionId;
        long stored = await StoredOutputBytesAsync(workflowId, cancellationToken);
        Workflow? executed = stored > MaxRunBytes
            ? null
            : await _userData.Workflows
                .GetAll(new WorkflowExecutionDetailByIdSpec())
                .Where(workflow => workflow.Id == workflowId
                    && workflow.OrganizationId == organizationId
                    && workflow.WorkflowTypeId == workflowTypeId)
                .SingleOrDefaultAsync(cancellationToken);

        EditorAgentReportSummary summary = (await SummariesAsync(organizationId, [execution.Row], cancellationToken))[0];

        string failure = string.Join(": ", new[] { execution.FailureKind, execution.FailureDetail }
            .Where(static part => !string.IsNullOrWhiteSpace(part)));
        return Evidence(
            summary,
            string.IsNullOrEmpty(failure) ? null : failure,
            executed,
            MaxRunBytes,
            budget,
            execution.Variables,
            stored > MaxRunBytes ? stored : null,
            openWorkflowType);
    }

    /// <summary>
    /// The size of what the run's blocks stored, their outputs and agent traces, read without
    /// loading them: a floor under the run file's size.
    /// </summary>
    private async Task<long> StoredOutputBytesAsync(string workflowId, CancellationToken cancellationToken)
    {
        long outputs = await _userData.Repository<BlockResponse>()
            .GetAll()
            .Where(response => response.Block != null && response.Block.Section != null && response.Block.Section.WorkflowId == workflowId)
            .SumAsync(static response => (long)(response.RawResponse ?? string.Empty).Length
                + (response.JsonResponse ?? string.Empty).Length
                + (response.MarkdownResponse ?? string.Empty).Length
                + (response.HtmlResponse ?? string.Empty).Length,
                cancellationToken);
        long traces = await _userData.Repository<AgentRunTraceFile>()
            .GetAll()
            .Where(file => file.Block != null && file.Block.Section != null && file.Block.Section.WorkflowId == workflowId)
            .SumAsync(static file => file.SizeBytes, cancellationToken);

        return outputs + traces;
    }

    private long MaxRunBytes => (long)_settings.MaxDraftMegabytes * BYTES_PER_MEGABYTE;

    private async Task<List<EditorAgentReportSummary>> SummariesAsync(
        string organizationId,
        IReadOnlyList<SummaryRow> rows,
        CancellationToken cancellationToken)
    {
        Dictionary<string, List<string>> documents = await DocumentNamesAsync(
            organizationId, [.. rows.Select(static row => row.JobId).OfType<string>()], cancellationToken);
        List<string> typeIds = [.. rows.Select(static row => row.WorkflowTypeId).Distinct(StringComparer.Ordinal)];
        Dictionary<string, string> typeNames = typeIds.Count == 0
            ? []
            : await _core.Repository<WorkflowType>()
                .GetAll()
                .AsNoTracking()
                .Where(type => typeIds.Contains(type.Id))
                .ToDictionaryAsync(static type => type.Id, static type => type.Name, StringComparer.Ordinal, cancellationToken);

        return [.. rows.Select(row => new EditorAgentReportSummary(
            row.Id,
            row.CompletedAt ?? row.CreatedAt,
            string.IsNullOrWhiteSpace(row.Counterparty) ? null : row.Counterparty,
            StatusOf(row.State),
            row.Title,
            row.JobId is not null && documents.TryGetValue(row.JobId, out List<string>? names) ? names : [])
        {
            WorkflowTypeId = row.WorkflowTypeId,
            WorkflowTypeName = typeNames.TryGetValue(row.WorkflowTypeId, out string? name) && !string.IsNullOrWhiteSpace(name)
                ? name
                : row.WorkflowTypeId,
        })];
    }

    public static AgentRunDocument ListDocument(string workflowTypeName, IReadOnlyList<EditorAgentReportSummary> reports)
    {
        return new AgentRunDocument(LIST_DOCUMENT, Encoding.UTF8.GetBytes(ListMarkdown(workflowTypeName, reports)), MARKDOWN_MEDIA_TYPE);
    }

    /// <param name="workflowTypeName">The open workflow's type, the one every listed report ran.</param>
    public static string ListMarkdown(string workflowTypeName, IReadOnlyList<EditorAgentReportSummary> reports)
    {
        var builder = new StringBuilder();
        _ = builder.AppendLine(string.Format(CultureInfo.InvariantCulture, LIST_TITLE, workflowTypeName)).AppendLine();
        if (reports.Count == 0)
        {
            _ = builder.AppendLine(NO_REPORTS_LINE);
            return builder.ToString();
        }

        _ = builder.AppendLine(LIST_INTRO).AppendLine();
        foreach (EditorAgentReportSummary report in reports)
        {
            _ = builder.AppendLine(CultureInfo.InvariantCulture,
                $"- id {report.Id}: {report.Date:yyyy-MM-dd HH:mm} UTC, {report.Counterparty ?? NO_COUNTERPARTY}, {report.Status}, documents: {DocumentLine(report.DocumentNames)}");
        }

        return builder.ToString();
    }


    /// <summary>
    /// One run's files: the executed workflow serialized as the template is, with every block's
    /// output, status and prompt as sent, and a header that names the run. A block copied per
    /// period, variant or loop item is marked with the template block it was copied from. A
    /// confidential variable's value is masked wherever the run holds it, as the run page masks
    /// it. A run past <paramref name="maxRunBytes"/>, or one that does not fit what the message may
    /// still stage, is not staged and the header says so; a large one is sent compressed.
    /// </summary>
    /// <param name="budget">What the message may still stage, charged for the files staged; null for no limit.</param>
    /// <param name="variables">The values the run was given, keyed by variable tag.</param>
    /// <param name="knownSize">The size of a run already known to be over the limit, which was therefore not loaded.</param>
    /// <param name="openWorkflowType">The open workflow's type, so the header says whether the run is of it; null says neither.</param>
    public static EditorAgentReportEvidence Evidence(
        EditorAgentReportSummary summary,
        string? failure,
        Workflow? executed,
        long maxRunBytes,
        EditorAgentReportBudget? budget = null,
        IReadOnlyDictionary<string, object>? variables = null,
        long? knownSize = null,
        EditorAgentOpenWorkflowType? openWorkflowType = null)
    {
        var documents = new List<AgentRunDocument>();
        var texts = new List<string> { summary.Title };
        if (!string.IsNullOrWhiteSpace(failure))
        {
            texts.Add(failure);
        }

        List<string> confidential = [];
        string runLine;
        if (knownSize is long oversize)
        {
            runLine = string.Format(CultureInfo.InvariantCulture, TOO_LARGE_LINE, Megabytes(oversize), Megabytes(maxRunBytes));
        }
        else if (executed is null)
        {
            runLine = NO_RUN_LINE;
        }
        else
        {
            executed.NormalizeSectionOrdering();
            foreach (Section section in executed.Sections)
            {
                section.NormalizeBlockOrdering();
            }

            EditorAgentRunVariables values = EditorAgentRunVariables.Of(executed.Variables, variables);
            confidential.AddRange(values.Confidential);
            JObject run = JsonText.ParseObject(executed.ToExportJson(includeRunTraces: true));
            values.Mask(run);
            int blocks = MarkCopies(run);
            byte[] bytes = Utf8Of(run);
            string size = Megabytes(bytes.LongLength);
            if (bytes.LongLength > maxRunBytes)
            {
                runLine = string.Format(CultureInfo.InvariantCulture, TOO_LARGE_LINE, size, Megabytes(maxRunBytes));
            }
            else
            {
                bool compress = bytes.LongLength > COMPRESS_ABOVE_BYTES;
                string name = RunDocumentName(summary.Id) + (compress ? GZIP_SUFFIX : string.Empty);
                AgentRunDocument document = compress
                    ? new AgentRunDocument(name, WorkflowJsonCompression.Compress(bytes), GZIP_MEDIA_TYPE)
                    : new AgentRunDocument(name, bytes, JSON_MEDIA_TYPE);
                if (budget is not null && !budget.Fits(document.Content.Length))
                {
                    runLine = string.Format(CultureInfo.InvariantCulture, OVER_BUDGET_LINE, size, EditorAgentReportFiles.BUDGET_USED);
                }
                else
                {
                    budget?.Charge(document.Content.Length);
                    documents.Add(document);
                    texts.AddRange(WorkflowRunEvidence.EvidenceStrings(run));
                    texts.AddRange(values.Plain);
                    runLine = string.Format(CultureInfo.InvariantCulture, compress ? COMPRESSED_RUN_LINE : RUN_LINE, name, blocks, size);
                }
            }
        }

        string header = EditorAgentRunVariables.Masked(HeaderMarkdown(summary, failure, runLine, openWorkflowType), confidential);
        byte[] headerBytes = Encoding.UTF8.GetBytes(header);
        budget?.Charge(headerBytes.LongLength);
        documents.Insert(0, new AgentRunDocument(HeaderDocumentName(summary.Id), headerBytes, MARKDOWN_MEDIA_TYPE));
        return new EditorAgentReportEvidence(summary, documents, [.. texts.Select(text => EditorAgentRunVariables.Masked(text, confidential))])
        {
            ConfidentialValues = confidential,
        };
    }

    private static byte[] Utf8Of(JObject run)
    {
        using var stream = new MemoryStream();
        using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true))
        using (var json = new Newtonsoft.Json.JsonTextWriter(writer) { Formatting = Newtonsoft.Json.Formatting.Indented })
        {
            run.WriteTo(json);
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Whether a report is a run of the open workflow, said outright: a report's type name can
    /// differ from the open workflow's own name, and the agent took such a run for another workflow.
    /// </summary>
    public static string TypeMark(EditorAgentReportSummary summary, EditorAgentOpenWorkflowType? openWorkflowType)
    {
        if (openWorkflowType is null)
        {
            return string.Empty;
        }

        if (string.Equals(summary.WorkflowTypeId, openWorkflowType.Id, StringComparison.Ordinal))
        {
            return OPEN_TYPE_MARK;
        }

        return openWorkflowType.CopyTypeIds.Contains(summary.WorkflowTypeId) ? COPY_TYPE_MARK : OTHER_TYPE_MARK;
    }

    /// <summary>
    /// The open workflow's type with every type in its line of duplicates: the root it was copied
    /// from, through any number of copies, and every type copied from that root, siblings and
    /// copies of copies included. A duplicate keeps every StableBlockId, so a run of any of them
    /// shares this workflow's block ids. The walk is bounded and stops at a cycle.
    /// </summary>
    public async Task<EditorAgentOpenWorkflowType> OpenWorkflowTypeAsync(string openWorkflowTypeId, CancellationToken cancellationToken)
    {
        List<(string Id, string Source)> links = [.. (await _core.Repository<WorkflowType>()
            .GetAll()
            .AsNoTracking()
            .Where(static type => type.DuplicatedFromWorkflowTypeId != null)
            .Select(static type => new { type.Id, Source = type.DuplicatedFromWorkflowTypeId! })
            .ToListAsync(cancellationToken))
            .Select(static link => (link.Id, link.Source))];
        Dictionary<string, string> sourceOf = links.ToDictionary(static link => link.Id, static link => link.Source, StringComparer.Ordinal);
        ILookup<string, string> copiesOf = links.ToLookup(static link => link.Source, static link => link.Id, StringComparer.Ordinal);

        string root = openWorkflowTypeId;
        var climbed = new HashSet<string>(StringComparer.Ordinal) { root };
        for (int depth = 0; depth < MAX_LINEAGE_DEPTH && sourceOf.TryGetValue(root, out string? source) && climbed.Add(source); depth++)
        {
            root = source;
        }

        var lineage = new HashSet<string>(StringComparer.Ordinal) { root };
        var pending = new Queue<(string Id, int Depth)>([(root, 0)]);
        while (pending.Count > 0)
        {
            (string id, int depth) = pending.Dequeue();
            if (depth >= MAX_LINEAGE_DEPTH)
            {
                continue;
            }

            foreach (string copy in copiesOf[id])
            {
                if (lineage.Add(copy))
                {
                    pending.Enqueue((copy, depth + 1));
                }
            }
        }

        _ = lineage.Remove(openWorkflowTypeId);
        return new EditorAgentOpenWorkflowType(openWorkflowTypeId, lineage);
    }

    public static string HeaderMarkdown(EditorAgentReportSummary summary, string? failure, string runLine, EditorAgentOpenWorkflowType? openWorkflowType = null)
    {
        var builder = new StringBuilder();
        _ = builder.AppendLine(CultureInfo.InvariantCulture, $"# Report {summary.Id}").AppendLine();
        _ = builder.AppendLine(CultureInfo.InvariantCulture, $"- Workflow type: {summary.WorkflowTypeName}{TypeMark(summary, openWorkflowType)}");
        _ = builder.AppendLine(CultureInfo.InvariantCulture, $"- Counterparty: {summary.Counterparty ?? NO_COUNTERPARTY}");
        _ = builder.AppendLine(CultureInfo.InvariantCulture, $"- Date: {summary.Date:yyyy-MM-dd HH:mm} UTC");
        _ = builder.AppendLine(CultureInfo.InvariantCulture, $"- Status: {summary.Status}");
        _ = builder.AppendLine(CultureInfo.InvariantCulture, $"- Title: {summary.Title}");
        if (!string.IsNullOrWhiteSpace(failure))
        {
            _ = builder.AppendLine(CultureInfo.InvariantCulture, $"- Failure: {failure}");
        }

        _ = builder.AppendLine(CultureInfo.InvariantCulture,
            $"- Documents: {(summary.DocumentNames.Count == 0 ? NO_DOCUMENTS : string.Join(", ", summary.DocumentNames))}");
        _ = builder.AppendLine().AppendLine(runLine);
        return builder.ToString();
    }

    /// <summary>
    /// Marks each block a section variant or a loop copied with <c>TemplateStableBlockId</c>, and
    /// each variant block with its <c>Variant</c> and <c>TemplateTag</c>, so a period copy reads
    /// back to the block it was copied from. Returns the number of blocks.
    /// </summary>
    private static int MarkCopies(JObject run)
    {
        int count = 0;
        foreach (JObject section in (run[SECTIONS_KEY] as JArray ?? []).OfType<JObject>())
        {
            string? variant = section[VARIANT_SOURCE_KEY]?.Value<string>();
            foreach (JObject block in (section[BLOCKS_KEY] as JArray ?? []).OfType<JObject>())
            {
                count++;
                if (block.Property(STABLE_ID_KEY) is not JProperty stable || stable.Value.Type != JTokenType.String)
                {
                    continue;
                }

                string stableId = stable.Value.Value<string>()!;
                int split = stableId.IndexOf('_', StringComparison.Ordinal);
                var marks = new List<JProperty>();
                if (split > 0 && !stableId.StartsWith(Block.LOOP_ITEM_STABLE_ID_PREFIX, StringComparison.Ordinal))
                {
                    marks.Add(new JProperty(TEMPLATE_STABLE_ID_KEY, stableId[..split]));
                }

                if (!string.IsNullOrWhiteSpace(variant))
                {
                    marks.Add(new JProperty(VARIANT_KEY, variant));
                    string suffix = "_" + variant;
                    if (block[TAG_KEY]?.Value<string>() is string tag && tag.EndsWith(suffix, StringComparison.Ordinal))
                    {
                        marks.Add(new JProperty(TEMPLATE_TAG_KEY, tag[..^suffix.Length]));
                    }
                }

                foreach (JProperty mark in Enumerable.Reverse(marks))
                {
                    stable.AddAfterSelf(mark);
                }
            }
        }

        return count;
    }

    private static string Megabytes(long bytes)
    {
        return (bytes / (double)BYTES_PER_MEGABYTE).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
    }

    private IQueryable<WorkflowExecution> Owned(string organizationId, IReadOnlyCollection<string> workflowTypeIds)
    {
        return _userData.Repository<WorkflowExecution>()
            .GetAll()
            .AsNoTracking()
            .Where(execution => execution.OrganizationId == organizationId
                && workflowTypeIds.Contains(execution.WorkflowTypeId)
                && !execution.IsDeleted
                && _listedStates.Contains(execution.WorkflowState));
    }

    private async Task<Dictionary<string, List<string>>> DocumentNamesAsync(
        string organizationId,
        IReadOnlyCollection<string> jobIds,
        CancellationToken cancellationToken)
    {
        if (jobIds.Count == 0)
        {
            return [];
        }

        var rows = await _userData.Repository<DocumentInfo>()
            .GetAll()
            .AsNoTracking()
            .Where(info => info.JobId != null
                && jobIds.Contains(info.JobId)
                && info.ExcludedAt == null
                && info.Document != null
                && info.Document.OrganizationId == organizationId)
            .Select(static info => new
            {
                JobId = info.JobId!,
                Name = info.Document!.FileMetadata != null
                    ? info.Document.FileMetadata.Name + info.Document.FileMetadata.Extension
                    : info.Document.Name,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Where(static row => !string.IsNullOrWhiteSpace(row.Name))
            .GroupBy(static row => row.JobId, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static row => row.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal);
    }

    private static string DocumentLine(IReadOnlyList<string> names)
    {
        if (names.Count == 0)
        {
            return NO_DOCUMENTS;
        }

        string shown = string.Join(", ", names.Take(MAX_DOCUMENT_NAMES));
        return names.Count > MAX_DOCUMENT_NAMES
            ? $"{shown} and {names.Count - MAX_DOCUMENT_NAMES} more"
            : shown;
    }

    private static string StatusOf(WorkflowState state)
    {
        return state switch
        {
            WorkflowState.Confirmed => CONFIRMED,
            WorkflowState.Failed => FAILED,
            WorkflowState.Cancelled => CANCELLED,
            _ => COMPLETED,
        };
    }

    private static WorkflowState? StateOf(string status)
    {
        return status.Trim().ToLowerInvariant() switch
        {
            COMPLETED => WorkflowState.Draft,
            CONFIRMED => WorkflowState.Confirmed,
            FAILED => WorkflowState.Failed,
            CANCELLED => WorkflowState.Cancelled,
            _ => null,
        };
    }


    private sealed record SummaryRow(
        string Id,
        string WorkflowTypeId,
        string Title,
        string? JobId,
        WorkflowState State,
        DateTime CreatedAt,
        DateTime? CompletedAt,
        string? Counterparty);
}

/// <param name="Documents">The header first, then the run file when it is staged.</param>
/// <param name="RunTexts">
/// What the run produced (every string outside the template's own fields: outputs, prompts and
/// inputs as sent, variable values, errors, traces) and its documents' text, for the check that
/// keeps it out of the workflow.
/// </param>
public sealed record EditorAgentReportEvidence(
    EditorAgentReportSummary Summary,
    IReadOnlyList<AgentRunDocument> Documents,
    IReadOnlyList<string> RunTexts)
{
    /// <summary>The values of the run's confidential variables, masked in what is staged and never allowed into the workflow.</summary>
    public IReadOnlyList<string> ConfidentialValues { get; init; } = [];
}

/// <param name="Id">The open workflow's type.</param>
/// <param name="CopyTypeIds">The other types in its line of duplicates, which share its block ids.</param>
public sealed record EditorAgentOpenWorkflowType(string Id, IReadOnlySet<string> CopyTypeIds);

/// <param name="Date">When the run finished, or started when it never did.</param>
/// <param name="Status">completed, confirmed, failed or cancelled.</param>
public sealed record EditorAgentReportSummary(
    string Id,
    DateTime Date,
    string? Counterparty,
    string Status,
    string Title,
    IReadOnlyList<string> DocumentNames)
{
    public string WorkflowTypeId { get; init; } = string.Empty;

    /// <summary>The workflow type's display name, such as "Corp Part 1 - Type A".</summary>
    public string WorkflowTypeName { get; init; } = string.Empty;
}

/// <summary>
/// What the report picker asks for. Every filter is optional; none at all is the open workflow's
/// latest reports.
/// </summary>
/// <param name="Status">completed, confirmed, failed or cancelled.</param>
/// <param name="From">The first day, inclusive, of the report date.</param>
/// <param name="To">The last day, inclusive, of the report date.</param>
/// <param name="WorkflowTypeId">Another type the organization has access to; null is the open workflow's type.</param>
public sealed record EditorAgentReportQuery(
    string? Search = null,
    string? Status = null,
    DateTime? From = null,
    DateTime? To = null,
    string? WorkflowTypeId = null,
    int Skip = 0,
    int Take = EditorAgentReports.LIST_SIZE);

public sealed record EditorAgentReportPage(IReadOnlyList<EditorAgentReportSummary> Reports, bool HasMore);

public sealed record EditorAgentWorkflowTypeOption(string Id, string Name);

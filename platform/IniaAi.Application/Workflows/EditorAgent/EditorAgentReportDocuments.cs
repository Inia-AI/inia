using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IniaAi.Common;
using IniaAi.Common.Extensions;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Configuration.Platform;
using IniaAi.Configuration.Storage;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Documents.Enums;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Enums;
using IniaAi.Domain.Specifications.Workflows;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IniaAi.Application.Workflows.EditorAgent;

/// <summary>
/// A staged report's documents: the files its run read and the files it produced, each as the
/// original and as its stored page text with page markers, so the agent greps the text and opens
/// only the pages it needs. Bounded to the organization that owns the run, and to the message's
/// <see cref="EditorAgentReportBudget"/>: what does not fit is listed in the header with its size
/// and the reason.
/// </summary>
public sealed class EditorAgentReportDocuments
{
    private readonly IUserDataUnitOfWork _userData;
    private readonly IFileStorageManager _storage;
    private readonly ILogger<EditorAgentReportDocuments> _logger;
    private readonly long _maxFileBytes;

    public EditorAgentReportDocuments(
        IUserDataUnitOfWork userData,
        IFileStorageManager storage,
        IOptions<EditorAgentSettings> settings,
        ILogger<EditorAgentReportDocuments> logger)
    {
        _userData = userData;
        _storage = storage;
        _logger = logger;
        _maxFileBytes = (long)settings.Value.MaxReportDocumentMegabytes * EditorAgentReportFiles.BYTES_PER_MEGABYTE;
    }

    /// <summary>
    /// The report with its documents staged beside its run. The report's own files were charged to
    /// the budget when they were built.
    /// </summary>
    public async Task<EditorAgentReportEvidence> AddAsync(
        string organizationId,
        EditorAgentReportEvidence evidence,
        EditorAgentReportBudget budget,
        CancellationToken cancellationToken)
    {
        var execution = await _userData.Repository<WorkflowExecution>()
            .GetAll()
            .AsNoTracking()
            .Where(row => row.Id == evidence.Summary.Id && row.OrganizationId == organizationId)
            .Select(static row => new { row.JobId, row.WorkflowId })
            .SingleOrDefaultAsync(cancellationToken);
        if (execution is null || string.IsNullOrEmpty(execution.JobId))
        {
            return evidence;
        }

        List<EditorAgentRunDocument> documents = await LoadAsync(
            organizationId, execution.JobId, execution.WorkflowId ?? evidence.Summary.Id, cancellationToken);
        EditorAgentReportFiles files = await EditorAgentReportFiles.BuildAsync(
            evidence.Summary.Id, documents, budget, _maxFileBytes, ReadAsync, cancellationToken);
        return files.AddTo(evidence);
    }

    /// <summary>
    /// The report with its documents' text added for the report-text check, nothing staged: a
    /// report an earlier turn of the conversation staged.
    /// </summary>
    public async Task<EditorAgentReportEvidence> WithTextsAsync(
        string organizationId,
        EditorAgentReportEvidence evidence,
        CancellationToken cancellationToken)
    {
        var execution = await _userData.Repository<WorkflowExecution>()
            .GetAll()
            .AsNoTracking()
            .Where(row => row.Id == evidence.Summary.Id && row.OrganizationId == organizationId)
            .Select(static row => new { row.JobId, row.WorkflowId })
            .SingleOrDefaultAsync(cancellationToken);
        if (execution is null || string.IsNullOrEmpty(execution.JobId))
        {
            return evidence;
        }

        List<EditorAgentRunDocument> documents = await LoadAsync(
            organizationId, execution.JobId, execution.WorkflowId ?? evidence.Summary.Id, cancellationToken);
        var texts = new List<string>(documents.SelectMany(static document => document.Pages).Select(static page => page.Text).OfType<string>());
        foreach (EditorAgentRunDocument document in documents.Where(EditorAgentReportFiles.IsOwnText))
        {
            if (document.SizeBytes <= _maxFileBytes && await ReadAsync(document, cancellationToken) is byte[] bytes)
            {
                texts.Add(Encoding.UTF8.GetString(bytes));
            }
        }

        return evidence with { RunTexts = [.. evidence.RunTexts, .. texts] };
    }

    /// <summary>
    /// The run's documents that belong to the organization, inputs then outputs, each with its
    /// pages in order. A document filed under one of the executed workflow's output classes is an
    /// output.
    /// </summary>
    private async Task<List<EditorAgentRunDocument>> LoadAsync(
        string organizationId,
        string jobId,
        string executedWorkflowId,
        CancellationToken cancellationToken)
    {
        HashSet<string> outputClasses = [.. await _userData.Repository<DocumentClass>()
            .GetAllAsync(new OutputDocumentClassKeysByWorkflowIdSpec(executedWorkflowId), cancellationToken)];

        var rows = await _userData.Repository<DocumentInfo>()
            .GetAll()
            .AsNoTracking()
            .Where(DocumentInfo.IsRunInputOf(jobId))
            .Where(info => info.Document != null
                && info.Document.OrganizationId == organizationId
                && info.Document.FileMetadata != null
                && info.Document.FileMetadata.OrganizationId == organizationId)
            .Select(static info => new
            {
                info.DocumentId,
                info.DocumentClass,
                info.Document!.Name,
                info.Document.MediaType,
                FileName = info.Document.FileMetadata!.Name,
                info.Document.FileMetadata.Extension,
                info.Document.FileMetadata.SizeBytes,
                info.Document.FileMetadata.StorageProvider,
                info.Document.FileMetadata.ExternalStoragePath,
            })
            .ToListAsync(cancellationToken);

        var unique = rows.GroupBy(static row => row.DocumentId, StringComparer.Ordinal).Select(static group => group.First()).ToList();
        List<string> ids = [.. unique.Select(static row => row.DocumentId)];
        List<Page> pages = ids.Count == 0
            ? []
            : await _userData.Repository<Page>()
                .GetAll()
                .AsNoTracking()
                .Where(page => ids.Contains(page.DocumentId))
                .ToListAsync(cancellationToken);
        Dictionary<string, List<EditorAgentRunPage>> pagesByDocument = pages
            .GroupBy(static page => page.DocumentId, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .OrderBy(static page => page.PageNumber)
                    .ThenBy(static page => page.Location)
                    .Select(static page => new EditorAgentRunPage(page.PageNumber, page.FilePageNumber, page.Location, PageText.ReadableOf(page)))
                    .ToList(),
                StringComparer.Ordinal);

        return [.. unique
            .Select(row => new EditorAgentRunDocument(
                row.DocumentId,
                string.IsNullOrWhiteSpace(row.FileName) ? row.Name : row.FileName.TrimEnd().WithExtension(row.Extension),
                row.DocumentClass,
                outputClasses.Contains(row.DocumentClass),
                row.MediaType.ToContentType(),
                row.SizeBytes,
                pagesByDocument.GetValueOrDefault(row.DocumentId) ?? [])
            {
                StorageProvider = row.StorageProvider.ToString(),
                StoragePath = row.ExternalStoragePath,
                OrganizationId = organizationId,
            })
            .OrderBy(static document => document.IsOutput)
            .ThenBy(static document => document.Name, StringComparer.OrdinalIgnoreCase)];
    }

    private async Task<byte[]?> ReadAsync(EditorAgentRunDocument document, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(document.StoragePath) || document.StorageProvider is null || document.OrganizationId is null)
        {
            return null;
        }

        try
        {
            return await _storage.RetrieveFileBytesAsync(
                document.StorageProvider, document.OrganizationId, StorageCategories.Documents, document.StoragePath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not read document {DocumentId} for the editor agent", document.Id);
            return null;
        }
    }
}

/// <summary>
/// What one message may still stage for report documents: bytes and files. Shared by the
/// reports the user picked and the ones the agent opens later in the message, so the start
/// request stays under the runtime's input budget and document count.
/// </summary>
public sealed class EditorAgentReportBudget
{
    public EditorAgentReportBudget(long bytes, int files)
    {
        BytesLeft = bytes;
        FilesLeft = files;
    }

    public long BytesLeft { get; private set; }

    public int FilesLeft { get; private set; }

    /// <summary>
    /// The message's budget: the setting, held under what the runtime admits once the turn's own
    /// files and skills are counted, with room left for the gate's files.
    /// </summary>
    public static EditorAgentReportBudget ForMessage(
        long settingBytes,
        IReadOnlyCollection<AgentRunDocument> staged,
        IEnumerable<AgentRunSkillFile> skills)
    {
        long used = staged.Sum(static document => (long)document.Content.Length) + AgentRunInputCapacity.SkillFileBytes(skills);
        long bytes = Math.Min(settingBytes, AgentRunInputCapacity.INPUT_BUDGET_BYTES - used - EditorAgentReportFiles.RESERVED_BYTES);
        int files = AgentRunInputCapacity.MAX_DOCUMENTS_PER_RUN - staged.Count - EditorAgentReportFiles.RESERVED_FILES;
        return new EditorAgentReportBudget(Math.Max(0, bytes), Math.Max(0, files));
    }

    public bool Fits(long bytes)
    {
        return FilesLeft > 0 && bytes <= BytesLeft;
    }

    /// <summary>Counts one staged file, whether or not it fit.</summary>
    public void Charge(long bytes)
    {
        BytesLeft -= bytes;
        FilesLeft--;
    }
}

/// <param name="Id">The document's id.</param>
/// <param name="Name">The file name with its extension.</param>
/// <param name="DocumentClass">The class key the run filed it under.</param>
/// <param name="SizeBytes">The stored size.</param>
public sealed record EditorAgentRunDocument(
    string Id,
    string Name,
    string DocumentClass,
    bool IsOutput,
    string MediaType,
    long SizeBytes,
    IReadOnlyList<EditorAgentRunPage> Pages)
{
    public string? StorageProvider { get; init; }

    public string? StoragePath { get; init; }

    public string? OrganizationId { get; init; }
}

/// <param name="Text">The page as the run read it: its text, or its overview when the page is image-only.</param>
public sealed record EditorAgentRunPage(int PageNumber, int FilePageNumber, PageLocation Location, string? Text);

/// <summary>
/// A report's documents as staged files: for each, the original and its page text with a
/// <c>=== marker page N ===</c> line before each page, where the marker is drawn at random when
/// the file is staged so a document's own text cannot pass for a page break, plus a manifest the
/// lookup helper reads and a header section that names every file and every one left out. An
/// original is staged only when the platform holds its text, so the report-text check knows
/// everything the agent reads. Runtime document names are flat, so the files
/// are named <c>report-&lt;id&gt;.in.&lt;name&gt;</c> and <c>report-&lt;id&gt;.out.&lt;name&gt;</c>.
/// </summary>
public sealed class EditorAgentReportFiles
{
    public const int BYTES_PER_MEGABYTE = 1024 * 1024;

    /// <summary>Kept free of report documents for the gate's own files and re-carried out/ files.</summary>
    public const long RESERVED_BYTES = 10L * BYTES_PER_MEGABYTE;

    public const int RESERVED_FILES = 6;
    public const string PAGES_SUFFIX = ".pages.txt";
    public const string MANIFEST_SUFFIX = ".documents.json";
    public const string INPUT_INFIX = ".in.";
    public const string OUTPUT_INFIX = ".out.";
    /// <summary>A page's first line: the document's marker, the page number and its notes.</summary>
    public const string PAGE_MARKER = "=== {0} page {1}{2} ===";
    public const string INPUT = "input";
    public const string OUTPUT = "output";
    public const string OVER_FILE_LIMIT = "the original is over the {0} per-file limit";
    public const string BUDGET_USED = "the message's limit for report documents is used up";
    public const string NOT_READABLE = "the original could not be read from storage";
    public const string NO_TEXT_TO_CHECK = "it has no stored text, so the platform cannot check what is copied from it";

    private const string REPORT_PREFIX = "report-";
    private const string TEXT_MEDIA_TYPE = "text/plain";
    private const string JSON_MEDIA_TYPE = "application/json";
    private const int MAX_NAME_STEM = 100;
    private const string SECTION_TITLE = "## Documents of the run";
    private const string SECTION_INTRO =
        "Each original sits beside its stored page text (`.pages.txt`, each page opened by a `=== <marker> page N ===` line, "
        + "the document's marker named in the manifest). Search the text with `wf.py grep {0} TEXT`, list the files with "
        + "`wf.py docs {0}`, and Read only the pages you need. An original without stored text is not staged.";
    private const string INPUTS_TITLE = "Input documents:";
    private const string OUTPUTS_TITLE = "Output documents:";
    private const string LEFT_OUT_TITLE = "Left out:";
    private const string NO_DOCUMENTS_LINE = "The run has no documents in your organization.";
    private const string NO_TEXT = "no stored text";
    private const int MARKER_BYTES = 6;

    private static readonly HashSet<string> _ownTextMediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/plain", "text/csv", "text/markdown", "application/json",
    };
    private const string NOT_STAGED = "not staged";
    private const string FILE_PAGE_NOTE = "file page ";
    private const string LEFT_SIDE_NOTE = "left side";
    private const string RIGHT_SIDE_NOTE = "right side";

    private static readonly JsonSerializerOptions _manifestOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private EditorAgentReportFiles(IReadOnlyList<AgentRunDocument> documents, string headerSection, IReadOnlyList<string> texts)
    {
        Documents = documents;
        HeaderSection = headerSection;
        Texts = texts;
    }

    public IReadOnlyList<AgentRunDocument> Documents { get; }

    /// <summary>The section appended to the report's header.</summary>
    public string HeaderSection { get; }

    /// <summary>Every document's text, staged or not, for the check that keeps report text out of the workflow.</summary>
    public IReadOnlyList<string> Texts { get; }

    public static string ManifestName(string executionId)
    {
        return REPORT_PREFIX + executionId + MANIFEST_SUFFIX;
    }

    /// <summary>
    /// Stages every page text first, since they are small and what the agent searches, then the
    /// originals in order while they fit. <paramref name="read"/> fetches an original's bytes and
    /// runs only for one that fits.
    /// </summary>
    public static async Task<EditorAgentReportFiles> BuildAsync(
        string executionId,
        IReadOnlyList<EditorAgentRunDocument> documents,
        EditorAgentReportBudget budget,
        long maxFileBytes,
        Func<EditorAgentRunDocument, CancellationToken, Task<byte[]?>> read,
        CancellationToken cancellationToken)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<Entry>();
        foreach (EditorAgentRunDocument document in documents)
        {
            string prefix = REPORT_PREFIX + executionId + (document.IsOutput ? OUTPUT_INFIX : INPUT_INFIX);
            string original = AgentStagedNames.Stage(prefix + ShortName(document.Name), used);
            string marker = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(MARKER_BYTES));
            entries.Add(new Entry(document, original, marker, PageText(document, marker)));
        }

        var staged = new List<AgentRunDocument>();
        var texts = new List<string>(documents.SelectMany(static document => document.Pages).Select(static page => page.Text).OfType<string>());
        foreach (Entry entry in entries.Where(static entry => entry.PageText is not null))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(entry.PageText!);
            if (!budget.Fits(bytes.LongLength))
            {
                entry.TextLeftOut = BUDGET_USED;
                continue;
            }

            budget.Charge(bytes.LongLength);
            entry.TextName = AgentStagedNames.Stage(entry.OriginalName + PAGES_SUFFIX, used);
            entry.TextBytes = bytes.LongLength;
            staged.Add(new AgentRunDocument(entry.TextName, bytes, TEXT_MEDIA_TYPE));
        }

        foreach (Entry entry in entries)
        {
            bool ownText = entry.PageText is null && IsOwnText(entry.Document);
            if (entry.PageText is null && !ownText)
            {
                entry.LeftOut = NO_TEXT_TO_CHECK;
                continue;
            }

            if (entry.Document.SizeBytes > maxFileBytes)
            {
                entry.LeftOut = string.Format(CultureInfo.InvariantCulture, OVER_FILE_LIMIT, Megabytes(maxFileBytes));
                continue;
            }

            if (!budget.Fits(entry.Document.SizeBytes))
            {
                entry.LeftOut = BUDGET_USED;
                continue;
            }

            byte[]? bytes = await read(entry.Document, cancellationToken);
            if (bytes is null || bytes.Length == 0)
            {
                entry.LeftOut = NOT_READABLE;
                continue;
            }

            if (bytes.LongLength > maxFileBytes || !budget.Fits(bytes.LongLength))
            {
                entry.LeftOut = bytes.LongLength > maxFileBytes
                    ? string.Format(CultureInfo.InvariantCulture, OVER_FILE_LIMIT, Megabytes(maxFileBytes))
                    : BUDGET_USED;
                continue;
            }

            budget.Charge(bytes.LongLength);
            entry.Staged = true;
            staged.Add(new AgentRunDocument(entry.OriginalName, bytes, entry.Document.MediaType));
            if (ownText)
            {
                texts.Add(Encoding.UTF8.GetString(bytes));
            }
        }

        if (entries.Count > 0)
        {
            byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(Manifest(entries), _manifestOptions);
            budget.Charge(manifest.LongLength);
            staged.Insert(0, new AgentRunDocument(ManifestName(executionId), manifest, JSON_MEDIA_TYPE));
        }

        return new EditorAgentReportFiles(staged, Section(executionId, entries), texts);
    }

    /// <summary>The report with these files after its run file and their section in its header.</summary>
    public EditorAgentReportEvidence AddTo(EditorAgentReportEvidence evidence)
    {
        List<AgentRunDocument> documents = [.. evidence.Documents];
        if (documents.Count > 0)
        {
            string header = Encoding.UTF8.GetString(documents[0].Content.Span).TrimEnd() + "\n\n" + HeaderSection;
            documents[0] = documents[0] with { Content = Encoding.UTF8.GetBytes(header) };
        }

        return evidence with
        {
            Documents = [.. documents, .. Documents],
            RunTexts = [.. evidence.RunTexts, .. Texts],
        };
    }

    /// <summary>A document whose original is plain text, so the original is its own text.</summary>
    public static bool IsOwnText(EditorAgentRunDocument document)
    {
        return _ownTextMediaTypes.Contains(document.MediaType);
    }

    /// <summary>The page text with a marker line before each page; null when no page has text.</summary>
    public static string? PageText(EditorAgentRunDocument document, string marker)
    {
        if (document.Pages.All(static page => string.IsNullOrWhiteSpace(page.Text)))
        {
            return null;
        }

        var builder = new StringBuilder();
        _ = builder.AppendLine(CultureInfo.InvariantCulture, $"{document.Name} ({(document.IsOutput ? OUTPUT : INPUT)}, {document.DocumentClass}), {document.Pages.Count} pages");
        foreach (EditorAgentRunPage page in document.Pages)
        {
            var notes = new List<string>();
            if (page.FilePageNumber > 0 && page.FilePageNumber != page.PageNumber)
            {
                notes.Add(FILE_PAGE_NOTE + page.FilePageNumber.ToString(CultureInfo.InvariantCulture));
            }

            if (page.Location != PageLocation.FullPage)
            {
                notes.Add(page.Location == PageLocation.LeftSide ? LEFT_SIDE_NOTE : RIGHT_SIDE_NOTE);
            }

            string note = notes.Count == 0 ? string.Empty : $" ({string.Join(", ", notes)})";
            _ = builder.AppendLine().AppendLine(string.Format(CultureInfo.InvariantCulture, PAGE_MARKER, marker, page.PageNumber, note));
            _ = builder.AppendLine((page.Text ?? string.Empty).TrimEnd());
        }

        return builder.ToString();
    }

    private static string Section(string executionId, List<Entry> entries)
    {
        var builder = new StringBuilder();
        _ = builder.AppendLine(SECTION_TITLE).AppendLine();
        if (entries.Count == 0)
        {
            _ = builder.AppendLine(NO_DOCUMENTS_LINE);
            return builder.ToString();
        }

        _ = builder.AppendLine(string.Format(CultureInfo.InvariantCulture, SECTION_INTRO, executionId));
        foreach ((string title, bool isOutput) in new[] { (INPUTS_TITLE, false), (OUTPUTS_TITLE, true) })
        {
            List<Entry> group = [.. entries.Where(entry => entry.Document.IsOutput == isOutput)];
            if (group.Count == 0)
            {
                continue;
            }

            _ = builder.AppendLine().AppendLine(title);
            foreach (Entry entry in group)
            {
                string original = entry.Staged ? $"`{entry.OriginalName}`" : NOT_STAGED;
                string text = entry.TextName is not null
                    ? $"text `{entry.TextName}` ({Size(entry.TextBytes)})"
                    : entry.TextLeftOut is not null ? "text " + NOT_STAGED : NO_TEXT;
                _ = builder.AppendLine(CultureInfo.InvariantCulture,
                    $"- {entry.Document.Name} ({entry.Document.DocumentClass}): {entry.Document.Pages.Count} pages, {Size(entry.Document.SizeBytes)}; original {original}; {text}");
            }
        }

        List<string> leftOut = [.. entries.SelectMany(static entry => LeftOutLines(entry))];
        if (leftOut.Count > 0)
        {
            _ = builder.AppendLine().AppendLine(LEFT_OUT_TITLE);
            foreach (string line in leftOut)
            {
                _ = builder.AppendLine(line);
            }
        }

        return builder.ToString();
    }

    private static IEnumerable<string> LeftOutLines(Entry entry)
    {
        string role = entry.Document.IsOutput ? OUTPUT : INPUT;
        if (entry.LeftOut is not null)
        {
            yield return $"- {entry.Document.Name} ({role} original, {Size(entry.Document.SizeBytes)}): {entry.LeftOut}.";
        }

        if (entry.TextLeftOut is not null)
        {
            yield return $"- {entry.Document.Name} ({role} page text, {Size(Encoding.UTF8.GetByteCount(entry.PageText ?? string.Empty))}): {entry.TextLeftOut}.";
        }
    }

    private static object Manifest(List<Entry> entries)
    {
        return new
        {
            Documents = entries.Select(static entry => new
            {
                Role = entry.Document.IsOutput ? OUTPUT : INPUT,
                entry.Document.Name,
                Class = entry.Document.DocumentClass,
                entry.Document.MediaType,
                Bytes = entry.Document.SizeBytes,
                Pages = entry.Document.Pages.Count,
                File = entry.Staged ? entry.OriginalName : null,
                Text = entry.TextName,
                PageMarker = entry.TextName is null ? null : entry.Marker,
                TextBytes = entry.TextName is null ? (long?)null : entry.TextBytes,
                LeftOut = entry.LeftOut,
                TextLeftOut = entry.TextLeftOut,
            }),
        };
    }

    /// <summary>The name cut to a bounded stem, keeping its extension, so the staged names stay under the runtime's limit.</summary>
    private static string ShortName(string name)
    {
        string extension = Path.GetExtension(name);
        string stem = Path.GetFileNameWithoutExtension(name);
        return (stem.Length > MAX_NAME_STEM ? stem[..MAX_NAME_STEM] : stem) + extension;
    }

    private static string Megabytes(long bytes)
    {
        return (bytes / (double)BYTES_PER_MEGABYTE).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
    }

    private static string Size(long bytes)
    {
        return bytes >= BYTES_PER_MEGABYTE
            ? Megabytes(bytes)
            : (bytes / 1024d).ToString("0", CultureInfo.InvariantCulture) + " KB";
    }

    private sealed class Entry(EditorAgentRunDocument document, string originalName, string marker, string? pageText)
    {
        public EditorAgentRunDocument Document { get; } = document;

        public string OriginalName { get; } = originalName;

        public string Marker { get; } = marker;

        public string? PageText { get; } = pageText;

        public string? TextName { get; set; }

        public long TextBytes { get; set; }

        public string? TextLeftOut { get; set; }

        public bool Staged { get; set; }

        public string? LeftOut { get; set; }
    }
}

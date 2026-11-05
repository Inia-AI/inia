using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using IniaAi.AiConnection.Abstractions;
using IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;
using IniaAi.Core.Workflows.Activity;
using Microsoft.Extensions.Logging;

namespace IniaAi.Core.Workflows.Agents;

/// <summary>
/// The cheap-model narration pass behind the activity feed: each of the agent's own words —
/// its progress notes, and the descriptions it wrote for its own tool calls — is rewritten as
/// one short client-register line, or suppressed when it carries nothing worth showing. The
/// two channels are prompted separately (see <see cref="TOOL_ACTION_SYSTEM_PROMPT"/>) and
/// batched one call per channel, never one call per line. The model receives only those text
/// lines with elapsed offsets — never the trace itself. A tool description is written by the
/// agent and routinely names paths and commands, which is exactly what the prompts strip. Results are cached by channel plus the raw
/// line's digest, so a line is paid for once across live re-publishes and replays. Fail-open
/// by contract: any error, timeout, or malformed response simply omits those keys, and the
/// feed shows the agent's raw words instead.
/// </summary>
public sealed partial class AgentNarrationTidier : IAgentNarrationTidier
{
    private const int RESPONSE_MAX_TOKENS = 1_000;
    private const int TIDY_TIMEOUT_SECONDS = 20;
    private const int CACHE_MAX_ENTRIES = 10_000;
    private const int TIDIED_LINE_MAX_CHARS = 120;
    private const string DROP_MARKER = "DROP";

    private const string NARRATION_SYSTEM_PROMPT =
        "You compress the working notes of a document-analysis agent into one-line activity-feed entries "
        + "a client reads while the agent works. The agent wrote these notes to itself; the reader knows "
        + "nothing about its tools or workspace. You receive numbered notes, each prefixed with its elapsed "
        + "time. For each note answer the single fact a reader cares about - one clause, plain professional "
        + "register, at most 80 characters.\n"
        + "Rules:\n"
        + "- One clause, not a compound sentence. When a note states several facts, keep the finding that "
        + "matters and drop the rest.\n"
        + "- Keep the finding, drop the procedure: what was learned survives; how it was done, and what "
        + "happens next, usually does not.\n"
        + "- Strip machinery vocabulary the reader cannot know: file paths, out/, manifest, working files, "
        + "structured output, JSON, and internal codes like FY0=2024 become plain language or vanish.\n"
        + "- Keep source names, page references, and figures the note itself states when they are the "
        + "finding.\n"
        + "- A note asserting a completed verification is already perfect: return it unchanged.\n"
        + "- If a note carries nothing a reader would learn, answer DROP - never pad.\n"
        + "- Never add anything the note does not say. Never merge notes.\n"
        + "Examples:\n"
        + "Note: Good, this confirms FY0=2024, FY-1=2023 data present. This is US GAAP, indirect method. "
        + "Now let's check the interest/tax paid notes and page number for image view.\n"
        + "Answer: Confirmed both years are present. Checking the interest and tax notes.\n"
        + "Note: Setting up to read manifest, convert PDFs, locate cash flow statement and notes, "
        + "cross-check dates, extract statement lines and decisions, and write output files.\n"
        + "Answer: Planning the extraction.\n"
        + "Note: All out/ files written and validated. Assembling the final structured output from these "
        + "files.\n"
        + "Answer: Assembling the answer.\n"
        + "Note: All files written and validated. Now let's verify the JSON files parse correctly, then "
        + "assemble the final output.\n"
        + "Answer: Running final checks.\n"
        + "Note: Reconciliation ties exactly for all three periods.\n"
        + "Answer: Reconciliation ties exactly for all three periods.\n"
        + "Note: Assets equal liabilities plus equity at both dates.\n"
        + "Answer: Assets equal liabilities plus equity at both dates.\n"
        + "Note: Now the reconciliation file:\n"
        + "Answer: DROP\n"
        + "Answer with exactly one line per input note, in the same order, formatted as "
        + "'<number>. <line or DROP>'.";

    /// <summary>
    /// A second prompt, not a widening of the first. A progress note is reflective prose the
    /// agent wrote after learning something, so the note prompt's central rule — keep the
    /// finding, drop the procedure — is right there and exactly wrong here: a tool-call
    /// description is an imperative about an action, where the procedure IS the whole content
    /// and there is no separate finding to rescue. One prompt carrying both rules would have
    /// to hedge the rule that does the work in each, so each channel gets its own.
    /// </summary>
    private const string TOOL_ACTION_SYSTEM_PROMPT =
        "You turn a document-analysis agent's own tool-call descriptions into one-line "
        + "activity-feed entries. The reader is a credit analyst or other financial professional, "
        + "watching the agent read company filings and extract the financial statements. They care "
        + "about the accounts: which statements, notes and schedules it is working through, which "
        + "figures and treatments it has found, what it is checking. They do not care, at all, about "
        + "its workspace, its tools, its files, or how it moves around.\n"
        + "You receive numbered descriptions, each prefixed with its elapsed time. Each one is an "
        + "imperative the agent wrote for an action it is about to take.\n"
        + "Rules:\n"
        + "- Answer what is happening, not what to do: rewrite the imperative as a present-tense "
        + "clause about the work in progress.\n"
        + "- Here the action IS the content - there is no finding hiding behind the procedure. Keep "
        + "the action and the accounts it touches; drop everything else.\n"
        + "- Strip machinery the reader cannot know: file and directory paths, file names of the "
        + "agent's own scratch and output files, tool and command names, line, page-file and byte "
        + "ranges, and parentheticals about tool restrictions.\n"
        + "- Strip data-format vocabulary too. JSON, CSV, schema, structured output, manifest, "
        + "parsing, valid, well-formed, file and directory are not words a credit analyst wants "
        + "in a progress line. Say what was done to the accounts instead - an agent checking its "
        + "output files parse correctly is checking its own figures.\n"
        + "- Keep the accounting nouns the description itself states: statement, note and schedule "
        + "names, period and year labels, and the names of the documents under analysis.\n"
        + "- One clause, plain professional register, at most 80 characters.\n"
        + "- If the description touches no financial content at all - listing a directory, checking "
        + "a manifest, orienting in the workspace, moving or tidying files - answer DROP. The "
        + "reader loses nothing by never seeing it, and a padded line is worse than no line. But "
        + "judge the accounts, not the vocabulary: a line wrapped in file or format words still "
        + "counts as financial content when it touches the figures, and is rewritten, not dropped.\n"
        + "- Never add anything the description does not say. Never merge descriptions. Never name a "
        + "statement, note or figure the description did not name.\n"
        + "Examples:\n"
        + "Description: Show workspace root, docs directory, and manifest\n"
        + "Answer: DROP\n"
        + "Description: Check out directory contents\n"
        + "Answer: DROP\n"
        + "Description: Convert both annual report PDFs to text with layout preserved\n"
        + "Answer: Preparing both annual reports for reading\n"
        + "Description: Locate balance sheet statement in 2025 report\n"
        + "Answer: Finding the 2025 balance sheet\n"
        + "Description: View lines 1-60 of converted 2025 text (Read tool restricted to PDFs)\n"
        + "Answer: Reading the 2025 statements\n"
        + "Description: View notes index continuation in 2025 report\n"
        + "Answer: Reading on through the notes index\n"
        + "Description: Search for the net capital requirement disclosure in note 18\n"
        + "Answer: Checking note 18 on net capital requirements\n"
        + "Description: Verify every output file in out/ is valid JSON and the footings tie\n"
        + "Answer: Checking the extracted figures foot correctly\n"
        + "Description: Write the extracted income statement lines to the output file\n"
        + "Answer: Recording the income statement lines\n"
        + "Answer with exactly one line per input description, in the same order, formatted as "
        + "'<number>. <line or DROP>'.";

    private readonly IChatConnector _chatConnector;
    private readonly ILogger<AgentNarrationTidier> _logger;
    private readonly ConcurrentDictionary<string, string?> _cache = new();

    public AgentNarrationTidier(IChatConnector chatConnector, ILogger<AgentNarrationTidier> logger)
    {
        _chatConnector = chatConnector;
        _logger = logger;
    }

    [GeneratedRegex(@"^\s*(\d+)[.)]\s*(.*)$")]
    private static partial Regex ResponseLineRegex();

    public async Task<IReadOnlyDictionary<string, string?>> TidyAsync(
        IReadOnlyList<AgentNarrationLine> lines,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, string?>();
        var misses = new Dictionary<AgentActivityLineKind, List<AgentNarrationLine>>();
        foreach (AgentNarrationLine line in lines)
        {
            if (_cache.TryGetValue(CacheKeyOf(line.Kind, line.Text), out string? cached))
            {
                result[line.Key] = cached;
            }
            else
            {
                if (!misses.TryGetValue(line.Kind, out List<AgentNarrationLine>? batch))
                {
                    batch = [];
                    misses[line.Kind] = batch;
                }

                batch.Add(line);
            }
        }

        // One call per channel, never one per line: a block's ~21 tool descriptions cost a
        // single request, and a channel whose call fails takes only its own lines down with it.
        // The channels go together rather than in turn — the live feed gives the whole pass a
        // few seconds, and a channel made to queue behind the other is a channel that gets cut
        // off and has to be paid for again on the next poll.
        List<Task<IReadOnlyDictionary<string, string?>>> passes =
            [.. misses.Select(entry => TidyUncachedAsync(entry.Key, entry.Value, cancellationToken))];
        foreach (IReadOnlyDictionary<string, string?> tidied in await Task.WhenAll(passes))
        {
            foreach ((string key, string? value) in tidied)
            {
                result[key] = value;
            }
        }

        return result;
    }

    private static string SystemPromptFor(AgentActivityLineKind kind)
    {
        return kind == AgentActivityLineKind.ToolAction
            ? TOOL_ACTION_SYSTEM_PROMPT
            : NARRATION_SYSTEM_PROMPT;
    }

    private async Task<IReadOnlyDictionary<string, string?>> TidyUncachedAsync(
        AgentActivityLineKind kind,
        List<AgentNarrationLine> misses,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(TIDY_TIMEOUT_SECONDS));

            DateTimeOffset origin = misses[0].At;
            var request = new StringBuilder();
            for (int index = 0; index < misses.Count; index++)
            {
                TimeSpan elapsed = misses[index].At - origin;
                _ = request
                    .Append(CultureInfo.InvariantCulture, $"{index + 1}. [{(int)elapsed.TotalMinutes}m{elapsed.Seconds:00}s] ")
                    .AppendLine(Flatten(misses[index].Text));
            }

            string? response = (await _chatConnector.GetResponseAsync(
                request.ToString(),
                SystemPromptFor(kind),
                RESPONSE_MAX_TOKENS,
                shouldUseAdvancedModel: false,
                cancellationToken: timeout.Token)).GetPlainText();

            if (response is null || response.StartsWith(IChatConnector.ERROR_PREFIX, StringComparison.Ordinal))
            {
                return new Dictionary<string, string?>();
            }

            return ParseResponse(kind, misses, response);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Narration tidy pass failed for {Count} {Kind} lines; the feed falls back to the agent's raw words",
                misses.Count,
                kind);

            return new Dictionary<string, string?>();
        }
    }

    private Dictionary<string, string?> ParseResponse(
        AgentActivityLineKind kind,
        List<AgentNarrationLine> misses,
        string response)
    {
        var byNumber = new Dictionary<int, string>();
        foreach (string responseLine in response.Split('\n'))
        {
            Match match = ResponseLineRegex().Match(responseLine);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int number))
            {
                byNumber[number] = match.Groups[2].Value.Trim();
            }
        }

        var tidied = new Dictionary<string, string?>();
        for (int index = 0; index < misses.Count; index++)
        {
            if (!byNumber.TryGetValue(index + 1, out string? value) || value.Length == 0)
            {
                continue;
            }

            string? display = value.Equals(DROP_MARKER, StringComparison.OrdinalIgnoreCase) ? null : value;
            if (display is not null)
            {
                if (!IsAcceptable(display, misses[index].Text))
                {
                    continue;
                }

                display = ActivityLineDiscipline.TrimToOneClause(display);
            }

            tidied[misses[index].Key] = display;

            // A rewrite is cached; a DROP deliberately is not. Suppression is measurably
            // non-deterministic across batch composition — the same description has answered
            // DROP in one batch and a good rewrite in another — and this cache is process-wide
            // on a singleton, so caching one unlucky roll would erase that wording from every
            // later run until restart, invisibly. Within a run the per-key settle in
            // LiveNarrationTidyState already keeps a drop stable, which is where it matters.
            if (display is not null && _cache.Count < CACHE_MAX_ENTRIES)
            {
                _cache[CacheKeyOf(kind, misses[index].Text)] = display;
            }
        }

        return tidied;
    }

    /// <summary>
    /// A tidy rewrites — it never grows a note or answers something else. An output longer
    /// than the raw note (past a small fixed allowance) or one that reads as a model
    /// refusal is rejected so the line falls back to the agent's raw words instead.
    /// </summary>
    private static bool IsAcceptable(string display, string rawText)
    {
        if (display.Length > Math.Max(TIDIED_LINE_MAX_CHARS, rawText.Length))
        {
            return false;
        }

        return !ActivityLineDiscipline.LooksLikeRefusal(display);
    }

    /// <summary>
    /// Keyed by channel as well as by words, because the two prompts answer the same sentence
    /// differently — a note's register is not a tool action's — and one channel must never be
    /// served the other's cached rewrite.
    /// </summary>
    private static string CacheKeyOf(AgentActivityLineKind kind, string rawText)
    {
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{kind}\n{rawText}")));
    }

    private static string Flatten(string text)
    {
        return string.Join(' ', text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}

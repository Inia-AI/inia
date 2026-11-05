using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using IniaAi.AiConnection.Abstractions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using Microsoft.Extensions.Logging;

namespace IniaAi.Core.Workflows.Activity;

/// <summary>
/// The once-per-version summarisation pass: the template's sections and blocks are briefed to
/// the cheap model in authored order, and the model answers a partition of contiguous groups,
/// one plain-language line each. Follows <see cref="Agents.AgentNarrationTidier"/>'s prompt
/// discipline — an 80-character clause, machinery vocabulary stripped, housekeeping dropped
/// rather than padded — but where the tidier rewrites per-run narration, this reads fixed
/// template properties, so the result is cacheable on the version row and costs nothing per
/// run. Every domain word in a line must come from the template's own content; the prompt
/// contributes register and rules only.
/// </summary>
public sealed partial class WorkflowActivityScriptWriter : IWorkflowActivityScriptWriter
{
    private const int RESPONSE_MAX_TOKENS = 2_000;
    private const int WRITE_TIMEOUT_SECONDS = 60;
    private const int BLOCK_GLOSS_MAX_CHARS = 240;
    private const string DROP_MARKER = "DROP";

    private const string SYSTEM_PROMPT =
        "You write the activity script for an automated document-analysis pipeline. While the "
        + "pipeline runs, a client reads one line at a time telling them what the system is working "
        + "on. You receive the pipeline as numbered internal build steps, each showing the author's "
        + "own names and configuration; the reader never sees these internals and knows nothing "
        + "about them.\n"
        + "Group consecutive steps that serve one job, and answer one line per group.\n"
        + "Rules:\n"
        + "- Cover every step exactly once: groups are contiguous ranges in order, starting at 1 "
        + "and ending at the last step, no gaps, no overlaps.\n"
        + "- Group generously: many consecutive small transformations serving one job are one "
        + "group. Aim for the fewest groups that still read as distinct pieces of work.\n"
        + "- One clause per group, present tense, plain professional register, at most 80 "
        + "characters: what the system is doing while that group runs.\n"
        + "- Use only vocabulary the steps themselves carry: name the documents, statements, "
        + "figures and subjects the steps name. Never invent facts the steps do not state.\n"
        + "- Strip machinery the reader cannot know: internal step and tag names, placeholder "
        + "syntax, JSON/format/template vocabulary, and model or tool names never appear in a "
        + "line.\n"
        + "- A line describes work, never results: never say a value was found, matched, verified "
        + "or correct.\n"
        + "- For a group that is internal housekeeping the reader would learn nothing from - debug "
        + "output, disabled steps, plumbing between steps - answer DROP.\n"
        + "Example answer for a 31-step pipeline:\n"
        + "1-3. Preparing the documents for reading\n"
        + "4-9. Locating the figures to extract\n"
        + "10-24. Standardising the extracted values\n"
        + "25-26. DROP\n"
        + "27-31. Assembling the final report\n"
        + "Answer with one line per group, formatted as '<first>-<last>. <line or DROP>', in "
        + "order.";

    private readonly IChatConnector _chatConnector;
    private readonly ILogger<WorkflowActivityScriptWriter> _logger;

    public WorkflowActivityScriptWriter(
        IChatConnector chatConnector,
        ILogger<WorkflowActivityScriptWriter> logger)
    {
        _chatConnector = chatConnector;
        _logger = logger;
    }

    [GeneratedRegex(@"^\s*(\d+)\s*(?:[-–]\s*(\d+))?\s*[.):]?\s+(.+?)\s*$")]
    private static partial Regex ResponseLineRegex();

    [GeneratedRegex(@"[{}]|\b\w+_\w+\b")]
    private static partial Regex MachinerySyntaxRegex();

    public async Task<WorkflowActivityScriptWriteResult> WriteScriptAsync(
        Workflow workflow,
        CancellationToken cancellationToken = default)
    {
        List<(string? StableBlockId, string Entry)> briefing = ComposeBriefing(workflow);
        if (briefing.Count == 0)
        {
            return WorkflowActivityScriptWriteResult.Declined;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(WRITE_TIMEOUT_SECONDS));

            var request = new StringBuilder();
            for (int index = 0; index < briefing.Count; index++)
            {
                _ = request
                    .Append(CultureInfo.InvariantCulture, $"{index + 1}. ")
                    .AppendLine(briefing[index].Entry);
            }

            string? response = (await _chatConnector.GetResponseAsync(
                request.ToString(),
                SYSTEM_PROMPT,
                RESPONSE_MAX_TOKENS,
                shouldUseAdvancedModel: false,
                cancellationToken: timeout.Token)).GetPlainText();

            if (response is null || response.StartsWith(IChatConnector.ERROR_PREFIX, StringComparison.Ordinal))
            {
                return WorkflowActivityScriptWriteResult.TransientFailure;
            }

            WorkflowActivityScript? script = ParseResponse(response, [.. briefing.Select(entry => entry.StableBlockId)]);
            return script is null
                ? WorkflowActivityScriptWriteResult.Declined
                : WorkflowActivityScriptWriteResult.Written(script);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Activity script pass failed for workflow type {WorkflowTypeId}; the run keeps its progress-only surface",
                workflow.WorkflowTypeId);

            return WorkflowActivityScriptWriteResult.TransientFailure;
        }
    }

    private static List<(string? StableBlockId, string Entry)> ComposeBriefing(Workflow workflow)
    {
        List<(string?, string)> briefing = [];
        foreach (Section section in workflow.Sections.OrderBy(section => section.Order))
        {
            foreach (Block block in section.Blocks.OrderBy(block => block.Order))
            {
                briefing.Add((block.StableBlockId, ComposeBlockEntry(section, block)));
            }
        }

        return briefing;
    }

    private static string ComposeBlockEntry(Section section, Block block)
    {
        string sectionTitle = string.IsNullOrWhiteSpace(section.Title) ? "(untitled)" : section.Title.Trim();
        string gloss = Flatten(ComposeBlockGloss(block));
        if (gloss.Length > BLOCK_GLOSS_MAX_CHARS)
        {
            gloss = gloss[..BLOCK_GLOSS_MAX_CHARS];
        }

        string entry = $"[{sectionTitle}] {block.Name} ({block.Type})";
        return gloss.Length == 0 ? entry : $"{entry}: {gloss}";
    }

    private static string ComposeBlockGloss(Block block)
    {
        if (block.Properties is AiQueryProperties aiQuery)
        {
            if (!string.IsNullOrWhiteSpace(aiQuery.UserPromptTemplatePreview))
            {
                return aiQuery.UserPromptTemplatePreview;
            }

            if (!string.IsNullOrWhiteSpace(aiQuery.UserPromptTemplate))
            {
                return aiQuery.UserPromptTemplate;
            }
        }

        return string.Join(" | ", block
            .GetAllTemplateStrings()
            .Where(template => !string.IsNullOrWhiteSpace(template)));
    }

    private WorkflowActivityScript? ParseResponse(string response, List<string?> blockIds)
    {
        List<WorkflowActivityGroup> groups = [];
        int expectedStart = 1;
        foreach (string responseLine in response.Split('\n'))
        {
            Match match = ResponseLineRegex().Match(responseLine);
            if (!match.Success)
            {
                continue;
            }

            if (!int.TryParse(match.Groups[1].Value, out int from))
            {
                continue;
            }

            int to = match.Groups[2].Success && int.TryParse(match.Groups[2].Value, out int parsedTo)
                ? parsedTo
                : from;

            if (from != expectedStart || to < from || to > blockIds.Count)
            {
                _logger.LogWarning(
                    "Activity script response did not partition the blocks (expected start {Expected}, got {From}-{To} of {Count})",
                    expectedStart,
                    from,
                    to,
                    blockIds.Count);

                return null;
            }

            groups.Add(new WorkflowActivityGroup(from - 1, to - 1, AcceptLabel(match.Groups[3].Value.Trim())));
            expectedStart = to + 1;
        }

        if (expectedStart != blockIds.Count + 1)
        {
            _logger.LogWarning(
                "Activity script response covered blocks 1-{Covered} of {Count} — truncated or incomplete; declining",
                expectedStart - 1,
                blockIds.Count);

            return null;
        }

        if (groups.TrueForAll(group => group.Label is null))
        {
            _logger.LogWarning(
                "Activity script response dropped every group of {Count} blocks; declining",
                blockIds.Count);

            return null;
        }

        return new WorkflowActivityScript(groups, blockIds);
    }

    private static string? AcceptLabel(string value)
    {
        if (value.Length == 0 || value.Equals(DROP_MARKER, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (ActivityLineDiscipline.LooksLikeRefusal(value) || MachinerySyntaxRegex().IsMatch(value))
        {
            return null;
        }

        return ActivityLineDiscipline.TrimToOneClause(value);
    }

    private static string Flatten(string text)
    {
        return string.Join(' ', text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;

namespace IniaAi.Application.Workflows.EditorAgent;

/// <summary>The icon keys of the editor agent's steps, drawn by the panel.</summary>
public static class EditorAgentStepIcons
{
    public const string SEARCH = "search";
    public const string OPEN = "open";
    public const string LINKS = "links";
    public const string REPORT = "report";
    public const string READ = "read";
    public const string CHECK = "check";
    public const string EDIT = "edit";
    public const string REMARK = "remark";
}

/// <summary>
/// Names the editor agent's steps in plain words, from the workflow and the reports the turn
/// staged: a <c>wf.py</c> lookup by what it looked up, any other call by the file it touched, and
/// the agent's remarks between steps as short plain sentences. Deterministic, so no step waits
/// for the narration tidier. A step never shows code, an id, a file path or JSON.
/// </summary>
public sealed partial class EditorAgentStepLabeller : IAgentActivityStepLabeller
{
    public const string SEARCHED_PROMPTS = "Searched the prompts for \"{0}\"";
    public const string SEARCHED_NAMES = "Searched the block names for \"{0}\"";
    public const string SEARCHED_WORKFLOW = "Searched the workflow for \"{0}\"";
    public const string SEARCHED_PLAIN = "Searched the workflow";
    public const string OPENED = "Opened {0}";
    public const string OPENED_A_BLOCK = "Opened a block";
    public const string OPENED_SECTION = "Opened the {0} section";
    public const string OPENED_A_SECTION = "Opened a section";
    public const string CHECKED_READERS = "Checked what reads {0}";
    public const string CHECKED_FEEDS = "Checked what feeds {0}";
    public const string LOOKED_AT_REPORT = "Looked at the {0} report";
    public const string LOOKED_AT_BLOCK_IN_REPORT = "Looked at {0} in the {1} report";
    public const string LOOKED_AT_A_REPORT = "Looked at a past report";
    public const string SEARCHED_REPORT = "Searched the {0} report for \"{1}\"";
    public const string READ_REPORT_LIST = "Read the list of past reports";
    public const string ASKED_FOR_REPORT = "Asked for a past report";
    public const string READ_OUTLINE = "Read the outline of the workflow";
    public const string READ_CONNECTIONS = "Looked at how blocks connect";
    public const string READ_VERSIONS = "Read the saved versions";
    public const string READ_GUIDE = "Read the workflow guide";
    public const string LOOKED_THROUGH = "Looked through the workflow";
    public const string CHECKED_CHANGES = "Checked the proposed changes";
    public const string PREPARED_CHANGES = "Prepared the proposed changes";
    public const string WROTE_SUMMARY = "Wrote down what the changes do";

    private const string A_BLOCK = "a block";
    private const int MAX_QUOTE_CHARS = 40;
    private const int MAX_REMARK_CHARS = 160;
    private const int MAX_NAMED_BLOCKS = 3;
    private const string ELLIPSIS = "…";
    private const string THEN = ", then ";
    private const string REPEATED = "{0} ×{1}";
    private const string AND = " and ";
    private const string LIST_SEPARATOR = ", ";
    private const string MORE_BLOCKS = "{0} more blocks";
    private const string HELPER_SCRIPT = "wf.py";
    private const string HELPER_VARIABLE = "$WF";
    private const string HELPER_VARIABLE_BRACED = "${WF}";
    private const string FULL_FLAG = "--full";
    private const string FIELD_FLAG = "--field";
    private const string REPORT_FLAG = "--report";
    private const string DOCS_FLAG = "--docs";
    private const string FIELD_NAME = "name";
    private const string FIELD_ANY = "any";
    private const string CMD_FIND = "find";
    private const string CMD_BLOCK = "block";
    private const string CMD_SECTION = "section";
    private const string CMD_READERS = "readers";
    private const string CMD_FEEDS = "feeds";
    private const string CMD_REPORT = "report";
    private const string CMD_REPORTS = "reports";
    private const string OUT_WORKFLOW = "out/workflow.json";
    private const string OUT_SUMMARY = "out/summary.json";
    private const string OPEN_REPORTS = "open-reports.json";
    private const string REPORT_PREFIX = "report-";
    private const string REPORT_DATE_FORMAT = "d MMM";
    private const string SKILL_FILE = "SKILL.md";

    private readonly Dictionary<string, Block> _byStableId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Block> _byTag = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Block> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Section> _sections;
    private readonly HashSet<string> _inputTags = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _reportDates = new(StringComparer.OrdinalIgnoreCase);

    public EditorAgentStepLabeller(Workflow workflow, IEnumerable<EditorAgentReportSummary> reports)
    {
        _sections = [.. workflow.Sections.OrderBy(static section => section.Order)];
        foreach (Block block in _sections.SelectMany(static section => section.Blocks.OrderBy(static block => block.Order)))
        {
            if (block.StableBlockId is string stableId)
            {
                _byStableId.TryAdd(stableId, block);
            }

            if (!string.IsNullOrWhiteSpace(block.ReplacementTag))
            {
                _byTag.TryAdd(block.ReplacementTag, block);
            }

            if (!string.IsNullOrWhiteSpace(block.Name))
            {
                _byName.TryAdd(block.Name, block);
            }
        }

        foreach (string tag in workflow.Variables.Select(static variable => variable.ReplacementTag)
            .Concat(workflow.DocumentClasses.Select(static documentClass => documentClass.Key)))
        {
            if (!string.IsNullOrWhiteSpace(tag))
            {
                _ = _inputTags.Add(tag);
            }
        }

        foreach (EditorAgentReportSummary report in reports)
        {
            _reportDates[report.Id] = report.Date;
        }
    }

    public AgentActivityStep? LabelToolCall(AgentActivityToolCall call)
    {
        if (call.ToolName == AgentTraceToolNames.READ)
        {
            return FileStep(call.FilePath ?? string.Empty) ?? new AgentActivityStep(LOOKED_THROUGH, EditorAgentStepIcons.READ);
        }

        if (call.ToolName != AgentTraceToolNames.BASH || string.IsNullOrWhiteSpace(call.Command))
        {
            return null;
        }

        string command = call.Command;
        List<HelperCall> lookups = [.. HelperCalls(command)];
        if (lookups.Count > 0)
        {
            return Joined(lookups);
        }

        if (command.Contains(EditorAgentStaging.CHECKER_ASSET, StringComparison.Ordinal))
        {
            return new AgentActivityStep(CHECKED_CHANGES, EditorAgentStepIcons.CHECK);
        }

        if (command.Contains(OPEN_REPORTS, StringComparison.Ordinal))
        {
            return new AgentActivityStep(ASKED_FOR_REPORT, EditorAgentStepIcons.REPORT);
        }

        if (command.Contains(OUT_WORKFLOW, StringComparison.Ordinal))
        {
            return new AgentActivityStep(PREPARED_CHANGES, EditorAgentStepIcons.EDIT);
        }

        if (command.Contains(OUT_SUMMARY, StringComparison.Ordinal))
        {
            return new AgentActivityStep(WROTE_SUMMARY, EditorAgentStepIcons.EDIT);
        }

        return FileStep(command) ?? new AgentActivityStep(LOOKED_THROUGH, EditorAgentStepIcons.SEARCH);
    }

    public AgentActivityStep? LabelRemark(string text)
    {
        string plain = BlockLinkTargetRegex().Replace(AgentActivityPlainText.From(text), string.Empty);
        plain = WhitespaceRegex().Replace(plain, " ").Trim();
        if (plain.Length == 0 || CodeLikeRegex().IsMatch(plain))
        {
            return AgentActivityStep.Hidden;
        }

        return new AgentActivityStep(Trimmed(plain, MAX_REMARK_CHARS), EditorAgentStepIcons.REMARK);
    }

    private AgentActivityStep? FileStep(string text)
    {
        if (ReportFileRegex().Match(text) is { Success: true } report)
        {
            return new AgentActivityStep(ReportLabel(report.Groups[1].Value, null), EditorAgentStepIcons.REPORT);
        }

        if (text.Contains(EditorAgentReports.LIST_DOCUMENT, StringComparison.Ordinal))
        {
            return new AgentActivityStep(READ_REPORT_LIST, EditorAgentStepIcons.REPORT);
        }

        if (text.Contains(EditorAgentStaging.DEPENDENCY_MAP_DOCUMENT, StringComparison.Ordinal))
        {
            return new AgentActivityStep(READ_CONNECTIONS, EditorAgentStepIcons.LINKS);
        }

        if (text.Contains(EditorAgentStaging.OUTLINE_DOCUMENT, StringComparison.Ordinal))
        {
            return new AgentActivityStep(READ_OUTLINE, EditorAgentStepIcons.READ);
        }

        if (text.Contains(EditorAgentStaging.VERSIONS_DOCUMENT, StringComparison.Ordinal))
        {
            return new AgentActivityStep(READ_VERSIONS, EditorAgentStepIcons.READ);
        }

        if (text.Contains(EditorAgentStaging.SKILL_NAME, StringComparison.Ordinal) || text.Contains(SKILL_FILE, StringComparison.Ordinal))
        {
            return new AgentActivityStep(READ_GUIDE, EditorAgentStepIcons.READ);
        }

        return text.Contains(EditorAgentStaging.WORKFLOW_DOCUMENT, StringComparison.Ordinal)
            ? new AgentActivityStep(LOOKED_THROUGH, EditorAgentStepIcons.SEARCH)
            : null;
    }

    /// <summary>Every <c>wf.py</c> call in the command, in order, each named on its own.</summary>
    private static IEnumerable<HelperCall> HelperCalls(string command)
    {
        foreach (string segment in SegmentRegex().Split(command))
        {
            List<string> tokens = Tokens(segment);
            int script = tokens.FindIndex(static token =>
                token.EndsWith(HELPER_SCRIPT, StringComparison.Ordinal)
                || token == HELPER_VARIABLE
                || token == HELPER_VARIABLE_BRACED);
            if (script < 0)
            {
                continue;
            }

            List<string> args = [.. tokens.Skip(script + 1)];
            if (args.Count >= 2 && args[0] == DOCS_FLAG)
            {
                args = [.. args.Skip(2)];
            }

            if (args.Count == 0)
            {
                continue;
            }

            string verb = args[0];
            string field = string.Empty;
            int fieldAt = args.IndexOf(FIELD_FLAG);
            if (fieldAt >= 0 && fieldAt + 1 < args.Count)
            {
                field = args[fieldAt + 1];
                args.RemoveRange(fieldAt, 2);
            }

            string? report = null;
            int reportAt = args.IndexOf(REPORT_FLAG);
            if (reportAt >= 0 && reportAt + 1 < args.Count)
            {
                report = args[reportAt + 1];
                args.RemoveRange(reportAt, 2);
            }

            List<string> rest = [.. args.Skip(1).Where(static arg => arg != FULL_FLAG)];
            yield return new HelperCall(verb, rest, field) { Report = report };
        }
    }

    private AgentActivityStep HelperStep(string verb, List<string> args, string field, string? report)
    {
        string joined = string.Join(' ', args);
        return verb switch
        {
            CMD_FIND when report is not null => new AgentActivityStep(ReportSearchLabel(report, joined), EditorAgentStepIcons.REPORT),
            CMD_FIND => new AgentActivityStep(SearchLabel(joined, field), EditorAgentStepIcons.SEARCH),
            CMD_BLOCK => new AgentActivityStep(OpenedLabel(args), EditorAgentStepIcons.OPEN),
            CMD_SECTION => new AgentActivityStep(SectionLabel(joined), EditorAgentStepIcons.OPEN),
            CMD_READERS => new AgentActivityStep(Format(CHECKED_READERS, NodeName(joined)), EditorAgentStepIcons.LINKS),
            CMD_FEEDS => new AgentActivityStep(Format(CHECKED_FEEDS, NodeName(joined)), EditorAgentStepIcons.LINKS),
            CMD_REPORT => new AgentActivityStep(
                ReportLabel(args.FirstOrDefault() ?? string.Empty, args.Count > 1 ? string.Join(' ', args.Skip(1)) : null),
                EditorAgentStepIcons.REPORT),
            CMD_REPORTS => new AgentActivityStep(READ_REPORT_LIST, EditorAgentStepIcons.REPORT),
            _ => new AgentActivityStep(LOOKED_THROUGH, EditorAgentStepIcons.SEARCH),
        };
    }

    /// <summary>
    /// Consecutive block lookups read as one; different lookups follow one another, and the same
    /// lookup repeated in a row reads once with its count ("Searched the workflow ×2").
    /// </summary>
    private AgentActivityStep Joined(List<HelperCall> lookups)
    {
        if (lookups.All(static lookup => lookup.Verb == CMD_BLOCK))
        {
            return HelperStep(CMD_BLOCK, [.. lookups.SelectMany(static lookup => lookup.Args)], string.Empty, null);
        }

        List<AgentActivityStep> steps = [.. lookups.Select(lookup => HelperStep(lookup.Verb, lookup.Args, lookup.Field, lookup.Report))];
        var runs = new List<(string Label, int Count)>();
        foreach (AgentActivityStep step in steps)
        {
            if (runs.Count > 0 && runs[^1].Label == step.Label)
            {
                runs[^1] = (step.Label, runs[^1].Count + 1);
            }
            else
            {
                runs.Add((step.Label, 1));
            }
        }

        var label = new StringBuilder();
        foreach ((string text, int count) in runs)
        {
            string part = count == 1 ? text : string.Format(CultureInfo.InvariantCulture, REPEATED, text, count);
            _ = label.Length == 0
                ? label.Append(part)
                : label.Append(THEN).Append(char.ToLowerInvariant(part[0])).Append(part[1..]);
        }

        return new AgentActivityStep(label.ToString(), steps[0].Icon);
    }

    private sealed record HelperCall(string Verb, List<string> Args, string Field)
    {
        public string? Report { get; init; }
    }

    private static string SearchLabel(string text, string field)
    {
        string quoted = text.Trim().Trim('"', '\'');
        if (quoted.Length == 0 || CodeLikeRegex().IsMatch(quoted) || IdLikeRegex().IsMatch(quoted))
        {
            return SEARCHED_PLAIN;
        }

        string template = field switch
        {
            FIELD_NAME => SEARCHED_NAMES,
            FIELD_ANY => SEARCHED_WORKFLOW,
            _ => SEARCHED_PROMPTS,
        };

        return Format(template, Trimmed(quoted, MAX_QUOTE_CHARS));
    }

    private string OpenedLabel(List<string> refs)
    {
        List<string> names = [.. refs.Select(BlockName).OfType<string>().Distinct(StringComparer.Ordinal)];
        return names.Count == 0 ? OPENED_A_BLOCK : Format(OPENED, NameList(names));
    }

    private static string NameList(List<string> names)
    {
        if (names.Count == 1)
        {
            return names[0];
        }

        if (names.Count <= MAX_NAMED_BLOCKS)
        {
            return string.Join(LIST_SEPARATOR, names[..^1]) + AND + names[^1];
        }

        return string.Join(LIST_SEPARATOR, names.Take(MAX_NAMED_BLOCKS - 1))
            + AND + Format(MORE_BLOCKS, (names.Count - MAX_NAMED_BLOCKS + 1).ToString(CultureInfo.InvariantCulture));
    }

    private string SectionLabel(string reference)
    {
        string trimmed = reference.Trim().Trim('"', '\'');
        Section? section = int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int order)
            ? _sections.FirstOrDefault(candidate => candidate.Order == order)
            : _sections.FirstOrDefault(candidate => trimmed.Length > 0
                && TitleOf(candidate).Contains(trimmed, StringComparison.OrdinalIgnoreCase));

        return section is null || string.IsNullOrWhiteSpace(TitleOf(section))
            ? OPENED_A_SECTION
            : Format(OPENED_SECTION, TitleOf(section));
    }

    private static string TitleOf(Section section)
    {
        return string.IsNullOrWhiteSpace(section.Title) ? section.Name : section.Title;
    }

    /// <summary>A block's name for a reference, or null when nothing names it safely.</summary>
    private string? BlockName(string reference)
    {
        string trimmed = reference.Trim().Trim('"', '\'', '{', '}');
        if (_byStableId.TryGetValue(trimmed, out Block? block)
            || _byTag.TryGetValue(trimmed, out block)
            || _byName.TryGetValue(trimmed, out block))
        {
            return block.Name;
        }

        Block? partial = trimmed.Length < 3
            ? null
            : _byName.Values.FirstOrDefault(candidate => candidate.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase));
        return partial?.Name;
    }

    private string NodeName(string reference)
    {
        if (BlockName(reference) is string name)
        {
            return name;
        }

        string trimmed = reference.Trim().Trim('"', '\'', '{', '}');
        return _inputTags.Contains(trimmed) && !IdLikeRegex().IsMatch(trimmed) ? trimmed : A_BLOCK;
    }

    private string ReportSearchLabel(string id, string text)
    {
        string quoted = text.Trim().Trim('"', '\'');
        if (!_reportDates.TryGetValue(id.Trim().Trim('"', '\''), out DateTime date)
            || quoted.Length == 0 || CodeLikeRegex().IsMatch(quoted) || IdLikeRegex().IsMatch(quoted))
        {
            return LOOKED_AT_A_REPORT;
        }

        return Format(SEARCHED_REPORT, date.ToString(REPORT_DATE_FORMAT, CultureInfo.InvariantCulture), Trimmed(quoted, MAX_QUOTE_CHARS));
    }

    private string ReportLabel(string id, string? block)
    {
        if (!_reportDates.TryGetValue(id.Trim().Trim('"', '\''), out DateTime date))
        {
            return LOOKED_AT_A_REPORT;
        }

        string day = date.ToString(REPORT_DATE_FORMAT, CultureInfo.InvariantCulture);
        return block is not null && BlockName(block) is string name
            ? Format(LOOKED_AT_BLOCK_IN_REPORT, name, day)
            : Format(LOOKED_AT_REPORT, day);
    }

    /// <summary>A command split shell-style: quotes group, a backslash escapes.</summary>
    private static List<string> Tokens(string segment)
    {
        List<string> tokens = [];
        var current = new StringBuilder();
        char quote = '\0';
        bool any = false;
        for (int index = 0; index < segment.Length; index++)
        {
            char character = segment[index];
            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                }
                else if (character == '\\' && quote == '"' && index + 1 < segment.Length)
                {
                    _ = current.Append(segment[++index]);
                }
                else
                {
                    _ = current.Append(character);
                }
            }
            else if (character is '"' or '\'')
            {
                quote = character;
                any = true;
            }
            else if (char.IsWhiteSpace(character))
            {
                if (any || current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    _ = current.Clear();
                    any = false;
                }
            }
            else
            {
                _ = current.Append(character);
            }
        }

        if (any || current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    private static string Trimmed(string text, int max)
    {
        if (text.Length <= max)
        {
            return text;
        }

        int cut = text.LastIndexOf(' ', max - 1);
        return text[..(cut < max / 2 ? max - 1 : cut)].TrimEnd() + ELLIPSIS;
    }

    private static string Format(string template, params object[] values)
    {
        return string.Format(CultureInfo.InvariantCulture, template, values);
    }

    [GeneratedRegex(@"[;\n]|&&|\|\|")]
    private static partial Regex SegmentRegex();

    [GeneratedRegex(@"report-([A-Za-z0-9-]+)\.(?:workflow\.json|md|json)")]
    private static partial Regex ReportFileRegex();

    [GeneratedRegex(@"\s*\(?block:[A-Za-z0-9_.:-]{3,}\)?")]
    private static partial Regex BlockLinkTargetRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    /// <summary>Paths, file names, code and JSON: a remark that carries any of them is not shown.</summary>
    [GeneratedRegex(@"[{}=<>\\$]|\w/\w|\.(json|py|txt|md|ndjson)\b|\b(python3?|jq|grep|sed|cat)\b|\b[0-9a-f]{16,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex CodeLikeRegex();

    [GeneratedRegex(@"^[0-9a-f-]{16,}$", RegexOptions.IgnoreCase)]
    private static partial Regex IdLikeRegex();
}

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;

/// <summary>
/// Projects a run's trace pack (NDJSON bytes) into the client-facing activity digest.
/// Tolerant by design: unparseable or truncated lines — mid-run reads end mid-line — are
/// skipped, and a pack missing whole record kinds degrades to fewer beats, never an error.
/// The projection is an allowlist: a beat exists only because a rule mapped a trace pattern
/// to client-shaped copy; an ambiguous call contributes nothing — a beat is not owed for
/// every record, and no path ever surfaces a raw command. Label precedence per beat: the
/// caller's tidied form of the agent's own words (its progress note, or the description it
/// wrote for a tool call), then those raw words, then the conservative neutral classification
/// in <see cref="AgentActivityMapping"/>. A line the caller's tidy map suppresses earns no
/// beat at all and leaves no placeholder behind. Beats derive from already-seen records only,
/// so a mid-run projection is always a prefix of the final one — re-polls can never reorder
/// or rewrite delivered beats.
/// </summary>
public static partial class AgentActivityProjector
{
    private const int LABEL_MAX_CHARS = 140;
    private const int DETAIL_MAX_CHARS = 200;
    // Raised with the quiet-kind throttle's removal. The trim below keeps the
    // EARLIEST beats and drops the rest, so an untrottled busy run would spend
    // its whole allowance in the opening minutes and then appear to stall.
    private const int MAX_BEATS = 400;
    private const int CHECK_AFTER_CODE_WINDOW_SECONDS = 60;
    private const int CONSECUTIVE_RETRY_WINDOW_SECONDS = 120;
    private const int CONSECUTIVE_HERO_WINDOW_SECONDS = 60;
    private const string TRUNCATION_ELLIPSIS = "…";
    private const string DETAIL_SEPARATOR = " · ";
    private const string CHECKLIST_MARKER = "Checklist";
    private const string MANIFEST_FILE_NAME = "manifest.json";

    private static readonly string[] _codeTokens = ["python3", "python", "node", "bun"];
    private static readonly string[] _searchTokens = ["grep", "rg"];
    private static readonly string[] _viewTokens = ["sed", "cat", "head", "tail", "less"];
    private const string CONVERT_TOKEN = "pdftotext";
    private const string CONVERTED_TEXT_SUFFIX = ".txt";

    private enum ActivityKind
    {
        None,
        LoadSkill,
        CheckDocumentSet,
        Convert,
        Search,
        ViewText,
        ViewPage,
        WriteWorkingFile,
        RunCode,
        ComposeAnswer,
    }

    private sealed record ToolFact(
        string Key,
        DateTimeOffset At,
        ActivityKind Kind,
        bool Errored,
        string? Description,
        string? SkillName,
        IReadOnlyList<string> DocumentNames,
        IReadOnlyList<string> WorkingFileNames,
        string? Pages)
    {
        public AgentActivityToolCall? Call { get; init; }
    }

    private sealed record NarrationFact(string Key, DateTimeOffset At, string Text);

    [GeneratedRegex("""docs/([A-Za-z0-9 ._\-]+?\.[A-Za-z0-9]{2,5})""")]
    private static partial Regex DocumentNameRegex();

    [GeneratedRegex(""""command"\s*:\s*"((?:[^"\\]|\\.)*)"""")]
    private static partial Regex SalvagedCommandRegex();

    [GeneratedRegex(""""description"\s*:\s*"((?:[^"\\]|\\.)*)"""")]
    private static partial Regex SalvagedDescriptionRegex();

    [GeneratedRegex(""">\s*([^\s>&|;]+)""")]
    private static partial Regex RedirectTargetRegex();

    public static AgentActivityDigest Project(ReadOnlySpan<byte> traceBytes)
    {
        return Project(traceBytes, AgentActivityProjectionOptions.Default);
    }

    public static AgentActivityDigest Project(
        ReadOnlySpan<byte> traceBytes,
        AgentActivityProjectionOptions options)
    {
        ParsedTrace trace = ParseTrace(traceBytes);
        DateTimeOffset? origin = trace.StartedAt ?? FirstTimestamp(trace.Facts);
        AgentActivityBeatSource source = ResolveSource(options.BeatSource, trace.Facts);
        return Assemble(trace, origin, source, options);
    }

    /// <summary>
    /// The agent's own text lines in time order — its progress notes and the descriptions it
    /// wrote for its own tool calls — the input a narration tidier receives. A description is
    /// itself a field of the tool input and routinely names paths and commands; nothing else of
    /// the trace is exposed. An errored call renders as the static retry label and never reads
    /// the tidy map, so its description is withheld rather than paid for and discarded.
    /// </summary>
    public static IReadOnlyList<AgentNarrationLine> ExtractNarration(ReadOnlySpan<byte> traceBytes)
    {
        return ExtractNarration(traceBytes, AgentActivityProjectionOptions.Default);
    }

    /// <summary>
    /// The lines a narration tidier still has to answer for: those the consumer's step labeller
    /// names itself, and remarks the projection will not show, are left out.
    /// </summary>
    public static IReadOnlyList<AgentNarrationLine> ExtractNarration(
        ReadOnlySpan<byte> traceBytes,
        AgentActivityProjectionOptions options)
    {
        List<object> facts = ParseTrace(traceBytes).Facts;
        int lastToolIndex = LastToolIndex(facts);
        return [.. facts
            .Select((fact, index) => (fact, index))
            .Where(entry => !ShownWithoutTidying(entry.fact, entry.index, lastToolIndex, options))
            .Select(static entry => entry.fact)
            .Select(static fact => fact switch
            {
                NarrationFact narration =>
                    new AgentNarrationLine(narration.Key, narration.At, narration.Text),
                ToolFact { Description: string description, Errored: false } tool =>
                    new AgentNarrationLine(tool.Key, tool.At, description)
                    {
                        Kind = AgentActivityLineKind.ToolAction,
                    },
                _ => null,
            })
            .OfType<AgentNarrationLine>()
            .OrderBy(static line => line.At)];
    }

    private static bool ShownWithoutTidying(
        object fact,
        int index,
        int lastToolIndex,
        AgentActivityProjectionOptions options)
    {
        if (fact is NarrationFact && options.RemarksBetweenToolCallsOnly && index > lastToolIndex)
        {
            return true;
        }

        return options.StepLabeller is IAgentActivityStepLabeller labeller && StepOf(fact, labeller) is not null;
    }

    private static AgentActivityStep? StepOf(object fact, IAgentActivityStepLabeller labeller)
    {
        return fact switch
        {
            ToolFact { Call: AgentActivityToolCall call, Errored: false } => labeller.LabelToolCall(call),
            NarrationFact narration => labeller.LabelRemark(narration.Text),
            _ => null,
        };
    }

    /// <summary>The position of the last tool call in time order; -1 when there is none.</summary>
    private static int LastToolIndex(List<object> facts)
    {
        return facts.FindLastIndex(static fact => fact is ToolFact);
    }

    private static DateTimeOffset AtOf(object fact)
    {
        return fact is ToolFact tool ? tool.At : ((NarrationFact)fact).At;
    }

    private sealed record ParsedTrace(
        List<object> Facts,
        DateTimeOffset? StartedAt,
        DateTimeOffset? SettledAt,
        AgentActivityOutcome Outcome,
        List<string> SkillNames);

    private static ParsedTrace ParseTrace(ReadOnlySpan<byte> traceBytes)
    {
        List<JsonObject> records = ParseRecords(traceBytes);

        DateTimeOffset? startedAt = null;
        DateTimeOffset? settledAt = null;
        AgentActivityOutcome outcome = records.Count > 0 ? AgentActivityOutcome.Running : AgentActivityOutcome.Unknown;
        List<string> skillNames = [];
        List<object> facts = [];

        for (int ordinal = 0; ordinal < records.Count; ordinal++)
        {
            JsonObject record = records[ordinal];
            switch (StringOf(record, "type"))
            {
                case AgentTraceRecordTypes.STAGE:
                    ReadStage(record, ref startedAt, ref settledAt, ref outcome);
                    break;
                case AgentTraceRecordTypes.ENVELOPE:
                    ReadEnvelopeSkills(record, skillNames);
                    break;
                case AgentTraceRecordTypes.TOOL_CALL:
                    if (ReadToolFact(record, ordinal) is ToolFact toolFact)
                    {
                        facts.Add(toolFact);
                    }

                    break;
                case AgentTraceRecordTypes.MESSAGE:
                    facts.AddRange(ReadNarrationFacts(record));
                    break;
                default:
                    break;
            }
        }

        return new ParsedTrace([.. facts.OrderBy(AtOf)], startedAt, settledAt, outcome, skillNames);
    }

    private static AgentActivityBeatSource ResolveSource(
        AgentActivityBeatSource requested,
        List<object> facts)
    {
        if (requested != AgentActivityBeatSource.Auto)
        {
            return requested;
        }

        // Every tool call earns a beat, whether or not the agent also narrated.
        // This used to answer Narrated once three narration lines existed, which
        // then discarded every tool beat but viewed-a-page and ran-code — so a
        // talkative run showed findings and nothing else, and long stretches of
        // searching looked like a stall.
        return AgentActivityBeatSource.Classified;
    }

    private static List<JsonObject> ParseRecords(ReadOnlySpan<byte> traceBytes)
    {
        List<JsonObject> records = [];
        foreach (string line in Encoding.UTF8.GetString(traceBytes).Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                if (JsonNode.Parse(line) is JsonObject parsed)
                {
                    records.Add(parsed);
                }
            }
            catch (System.Text.Json.JsonException)
            {
                continue;
            }
        }

        return records;
    }

    private static void ReadStage(
        JsonObject record,
        ref DateTimeOffset? startedAt,
        ref DateTimeOffset? settledAt,
        ref AgentActivityOutcome outcome)
    {
        DateTimeOffset? at = TimestampOf(record);
        switch (StringOf(record, "event"))
        {
            case AgentTraceStageEvents.RUN_STARTED:
                startedAt ??= at;
                break;
            case AgentTraceStageEvents.RUN_COMPLETED:
                settledAt = at;
                outcome = AgentActivityOutcome.Succeeded;
                break;
            case AgentTraceStageEvents.RUN_FAILED:
                settledAt = at;
                outcome = AgentActivityOutcome.Failed;
                break;
            default:
                break;
        }
    }

    private static void ReadEnvelopeSkills(JsonObject record, List<string> skillNames)
    {
        if (record["skills"] is not JsonArray skills)
        {
            return;
        }

        foreach (JsonNode? entry in skills)
        {
            if (entry?.GetValueKind() == System.Text.Json.JsonValueKind.String
                && entry.GetValue<string>() is string qualified)
            {
                string name = qualified[(qualified.LastIndexOf(':') + 1)..];
                if (name.Length > 0 && !skillNames.Contains(name))
                {
                    skillNames.Add(name);
                }
            }
        }
    }

    /// <summary>
    /// The tool call's stable cache identity: its trace record's uuid plus that record's
    /// position in the pack. The pack is append-only and only its final line can be truncated
    /// mid-read, so a record's position never shifts under a later poll — which is what lets
    /// the live feed tidy a description once and keep the answer for the run's whole life.
    /// The position also keeps the key unique when a pack omits uuids.
    /// </summary>
    private static string ToolKey(JsonObject record, int ordinal)
    {
        return $"{StringOf(record, "uuid") ?? string.Empty}:{ordinal.ToString(CultureInfo.InvariantCulture)}";
    }

    private static ToolFact? ReadToolFact(JsonObject record, int ordinal)
    {
        if (TimestampOf(record) is not DateTimeOffset at)
        {
            return null;
        }

        string key = ToolKey(record, ordinal);
        string? toolName = StringOf(record, "toolName");
        bool errored = StringOf(record, "errorClass") is not null;
        string input = StringOf(record, "inputTruncated") ?? string.Empty;
        JsonObject? parsedInput = TryParseInput(input);

        return toolName switch
        {
            AgentTraceToolNames.BASH => BashFact(key, at, errored, input, parsedInput),
            AgentTraceToolNames.READ => ReadToolReadFact(key, at, errored, input, parsedInput),
            "Skill" => new ToolFact(
                key, at, ActivityKind.LoadSkill, errored, null,
                StringOf(parsedInput, "skill"), [], [], null),
            "StructuredOutput" => new ToolFact(
                key, at, ActivityKind.ComposeAnswer, errored, null, null, [], [], null),
            _ => null,
        };
    }

    private static ToolFact BashFact(
        string key,
        DateTimeOffset at,
        bool errored,
        string rawInput,
        JsonObject? parsedInput)
    {
        string command = StringOf(parsedInput, "command")
            ?? Unescape(SalvagedCommandRegex().Match(rawInput).Groups[1].Value);
        string? description = StringOf(parsedInput, "description")
            ?? NullIfEmpty(Unescape(SalvagedDescriptionRegex().Match(rawInput).Groups[1].Value));

        (ActivityKind kind, IReadOnlyList<string> workingFiles) = ClassifyCommand(command);
        return new ToolFact(
            key, at, kind, errored, description, null, DocumentNamesIn(command), workingFiles, null)
        {
            Call = new AgentActivityToolCall(AgentTraceToolNames.BASH, command, null),
        };
    }

    private static ToolFact ReadToolReadFact(
        string key,
        DateTimeOffset at,
        bool errored,
        string rawInput,
        JsonObject? parsedInput)
    {
        string? pages = parsedInput?["pages"]?.ToString();
        string filePath = StringOf(parsedInput, "file_path") ?? rawInput;
        ActivityKind kind = pages is null ? ActivityKind.ViewText : ActivityKind.ViewPage;
        return new ToolFact(key, at, kind, errored, null, null, DocumentNamesIn(filePath), [], pages)
        {
            Call = new AgentActivityToolCall(AgentTraceToolNames.READ, null, filePath),
        };
    }

    private static (ActivityKind Kind, IReadOnlyList<string> WorkingFileNames) ClassifyCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return (ActivityKind.None, []);
        }

        List<string> workingFiles = [];
        foreach (Match redirect in RedirectTargetRegex().Matches(command))
        {
            string target = redirect.Groups[1].Value;
            if (!target.StartsWith(AgentRunFilePaths.ARTIFACT_PREFIX, StringComparison.Ordinal))
            {
                continue;
            }

            string name = target[AgentRunFilePaths.ARTIFACT_PREFIX.Length..];
            if (!workingFiles.Contains(name))
            {
                workingFiles.Add(name);
            }
        }

        bool runsCode = false;
        bool converts = false;
        bool searches = false;
        bool views = false;

        foreach (string segment in SplitSegments(command))
        {
            string[] tokens = segment.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length == 0)
            {
                continue;
            }

            string leading = tokens[0];
            if (_codeTokens.Contains(leading))
            {
                runsCode = true;
            }
            else if (leading == CONVERT_TOKEN
                && tokens.Any(static token => token.EndsWith(CONVERTED_TEXT_SUFFIX, StringComparison.Ordinal)))
            {
                converts = true;
            }
            else if (_searchTokens.Contains(leading))
            {
                searches = true;
            }
            else if (_viewTokens.Contains(leading)
                && !segment.Contains('>')
                && tokens.Skip(1).Any(static token => !token.StartsWith('-')))
            {
                views = true;
            }
        }

        if (runsCode)
        {
            return (ActivityKind.RunCode, workingFiles);
        }

        if (workingFiles.Count > 0)
        {
            return (ActivityKind.WriteWorkingFile, workingFiles);
        }

        if (command.Contains(MANIFEST_FILE_NAME, StringComparison.Ordinal))
        {
            return (ActivityKind.CheckDocumentSet, []);
        }

        if (converts)
        {
            return (ActivityKind.Convert, []);
        }

        if (searches)
        {
            return (ActivityKind.Search, []);
        }

        return views ? (ActivityKind.ViewText, []) : (ActivityKind.None, []);
    }

    private static IEnumerable<string> SplitSegments(string command)
    {
        return command
            .Split('\n')
            .SelectMany(static line => line.Split("&&", StringSplitOptions.TrimEntries))
            .SelectMany(static piece => piece.Split("||", StringSplitOptions.TrimEntries))
            .SelectMany(static piece => piece.Split(';', StringSplitOptions.TrimEntries))
            .SelectMany(static piece => piece.Split('|', StringSplitOptions.TrimEntries));
    }

    private static IReadOnlyList<string> DocumentNamesIn(string text)
    {
        List<string> names = [];
        foreach (Match match in DocumentNameRegex().Matches(text))
        {
            string name = match.Groups[1].Value.Trim();
            if (name.Length > 0 && name != MANIFEST_FILE_NAME && !names.Contains(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static IEnumerable<NarrationFact> ReadNarrationFacts(JsonObject record)
    {
        if (TimestampOf(record) is not DateTimeOffset at
            || record["message"] is not JsonObject wrapper
            || StringOf(wrapper, "type") != "assistant"
            || wrapper["message"] is not JsonObject payload
            || payload["content"] is not JsonArray content)
        {
            yield break;
        }

        string recordUuid = StringOf(record, "uuid") ?? string.Empty;
        for (int blockIndex = 0; blockIndex < content.Count; blockIndex++)
        {
            if (content[blockIndex] is JsonObject blockObject
                && StringOf(blockObject, "type") == "text"
                && StringOf(blockObject, "text") is string text
                && !string.IsNullOrWhiteSpace(text))
            {
                yield return new NarrationFact($"{recordUuid}:{blockIndex}", at, text);
            }
        }
    }

    private static DateTimeOffset? FirstTimestamp(List<object> facts)
    {
        return facts.Count == 0
            ? null
            : facts.Min(static fact => fact is ToolFact tool ? tool.At : ((NarrationFact)fact).At);
    }

    private static AgentActivityDigest Assemble(
        ParsedTrace trace,
        DateTimeOffset? origin,
        AgentActivityBeatSource source,
        AgentActivityProjectionOptions options)
    {
        IReadOnlyDictionary<string, string?>? tidiedNarration = options.TidiedNarration;
        IAgentActivityStepLabeller? labeller = options.StepLabeller;
        List<object> facts = trace.Facts;
        int lastToolIndex = LastToolIndex(facts);
        DateTimeOffset? startedAt = trace.StartedAt;
        DateTimeOffset? settledAt = trace.SettledAt;
        AgentActivityOutcome outcome = trace.Outcome;
        List<string> skillNames = trace.SkillNames;
        List<AgentActivityBeat> beats = [];
        List<string> documentNames = [];
        int phase = AgentActivityMapping.PHASE_GETTING_READY;
        int pagesViewed = 0;
        int codeRuns = 0;
        DateTimeOffset? uninterruptedCodeRunAt = null;
        DateTimeOffset? lastRetryAt = null;
        ActivityKind lastEmittedKind = ActivityKind.None;
        DateTimeOffset? lastEmittedAt = null;

        for (int index = 0; index < facts.Count; index++)
        {
            object fact = facts[index];
            if (beats.Count == MAX_BEATS)
            {
                break;
            }

            if (fact is NarrationFact && options.RemarksBetweenToolCallsOnly && index > lastToolIndex)
            {
                continue;
            }

            if (labeller is not null && StepOf(fact, labeller) is AgentActivityStep step)
            {
                if (fact is ToolFact labelled)
                {
                    phase = AdvancePhase(phase, labelled.Kind);
                    uninterruptedCodeRunAt = null;
                }

                if (step.Label.Length > 0)
                {
                    DateTimeOffset at = AtOf(fact);
                    beats.Add(new AgentActivityBeat(
                        OffsetSeconds(at, origin), phase, AgentActivityBeatKind.Normal,
                        Cap(AgentActivityPlainText.From(step.Label), LABEL_MAX_CHARS), null)
                    {
                        Icon = step.Icon,
                    });
                    lastEmittedKind = ActivityKind.None;
                    lastEmittedAt = at;
                }

                continue;
            }

            if (fact is NarrationFact narration)
            {
                if (NarrationBeat(narration, origin, phase, uninterruptedCodeRunAt, tidiedNarration)
                    is AgentActivityBeat narrated)
                {
                    beats.Add(narrated);
                    lastEmittedKind = ActivityKind.None;
                    lastEmittedAt = narration.At;
                }

                continue;
            }

            ToolFact tool = (ToolFact)fact;
            foreach (string documentName in tool.DocumentNames)
            {
                if (!documentNames.Contains(documentName))
                {
                    documentNames.Add(documentName);
                }
            }

            phase = AdvancePhase(phase, tool.Kind);
            if (tool.Kind == ActivityKind.ViewPage && !tool.Errored)
            {
                pagesViewed++;
            }

            uninterruptedCodeRunAt = tool.Kind == ActivityKind.RunCode && !tool.Errored ? tool.At : null;
            if (uninterruptedCodeRunAt is not null)
            {
                codeRuns++;
            }

            if (tool.Errored)
            {
                bool collapseIntoPreviousRetry = lastRetryAt is DateTimeOffset previousRetry
                    && (tool.At - previousRetry).TotalSeconds <= CONSECUTIVE_RETRY_WINDOW_SECONDS;
                lastRetryAt = tool.At;
                if (!collapseIntoPreviousRetry)
                {
                    beats.Add(new AgentActivityBeat(
                        OffsetSeconds(tool.At, origin), phase, AgentActivityBeatKind.Normal,
                        AgentActivityMapping.RETRY_LABEL, null));
                    lastEmittedKind = ActivityKind.None;
                    lastEmittedAt = tool.At;
                }

                continue;
            }

            if (tool.Description is string description)
            {
                string? displayText = DisplayTextOf(tool.Key, description, tidiedNarration);
                if (displayText is null)
                {
                    // Suppressed: the tidier judged this call carries nothing for this reader.
                    // No beat, no placeholder — the beats around it keep their own offsets, so
                    // the removal reads as a quiet stretch rather than a blank row. Never fall
                    // through to the neutral classification here, which would resurrect the
                    // very line the tidier just refused.
                    continue;
                }

                AgentActivityBeatKind kind = tool.Kind == ActivityKind.RunCode
                    ? AgentActivityBeatKind.Hero
                    : AgentActivityBeatKind.Normal;
                beats.Add(new AgentActivityBeat(
                    OffsetSeconds(tool.At, origin), phase, kind, Cap(AgentActivityPlainText.From(displayText), LABEL_MAX_CHARS),
                    DetailFor(tool))
                {
                    DocumentNames = tool.DocumentNames,
                });
                lastEmittedKind = tool.Kind;
                lastEmittedAt = tool.At;
                continue;
            }

            if (tool.Kind == ActivityKind.None)
            {
                continue;
            }

            // Loading a skill is machinery, not work a reader benefits from seeing —
            // and it exposes the skill's internal name. It stayed hidden by accident
            // while the Narrated gate dropped every non-hero beat; now it is explicit.
            if (tool.Kind == ActivityKind.LoadSkill)
            {
                continue;
            }

            bool duplicateHero = tool.Kind == ActivityKind.RunCode
                && lastEmittedKind == ActivityKind.RunCode
                && lastEmittedAt is DateTimeOffset previousHero
                && (tool.At - previousHero).TotalSeconds < CONSECUTIVE_HERO_WINDOW_SECONDS;
            if (duplicateHero)
            {
                continue;
            }

            beats.Add(ClassifiedBeat(tool, origin, phase));
            lastEmittedKind = tool.Kind;
            lastEmittedAt = tool.At;
        }

        int? durationSeconds = startedAt is DateTimeOffset started && settledAt is DateTimeOffset settled
            ? (int)Math.Round((settled - started).TotalSeconds)
            : null;

        return new AgentActivityDigest
        {
            Outcome = outcome,
            StartedAtUtc = origin,
            Phases = AgentActivityMapping.Phases,
            Beats = beats,
            Receipt = new AgentActivityReceipt
            {
                DurationSeconds = durationSeconds,
                DocumentNames = documentNames,
                SkillNames = skillNames,
                PagesViewed = pagesViewed,
                CodeRuns = codeRuns,
                ChecksReported = beats.Count(static beat => beat.Kind == AgentActivityBeatKind.Check),
            },
        };
    }

    private static AgentActivityBeat? NarrationBeat(
        NarrationFact narration,
        DateTimeOffset? origin,
        int phase,
        DateTimeOffset? uninterruptedCodeRunAt,
        IReadOnlyDictionary<string, string?>? tidiedNarration)
    {
        int atSeconds = OffsetSeconds(narration.At, origin);

        if (narration.Text.StartsWith(CHECKLIST_MARKER, StringComparison.Ordinal))
        {
            return new AgentActivityBeat(
                atSeconds, phase, AgentActivityBeatKind.Normal,
                AgentActivityMapping.PLANNED_THE_WORK_LABEL, null);
        }

        bool isCheck = uninterruptedCodeRunAt is DateTimeOffset codeRanAt
            && (narration.At - codeRanAt).TotalSeconds is >= 0 and <= CHECK_AFTER_CODE_WINDOW_SECONDS;

        if (isCheck)
        {
            (string checkLabel, string? checkDetail) = SplitNarration(Flatten(AgentActivityPlainText.From(narration.Text)));
            return new AgentActivityBeat(
                atSeconds, phase, AgentActivityBeatKind.Check, checkLabel, checkDetail);
        }

        if (DisplayTextOf(narration.Key, narration.Text, tidiedNarration) is not string displayText)
        {
            return null;
        }

        (string label, string? detail) = SplitNarration(Flatten(AgentActivityPlainText.From(displayText)));
        return new AgentActivityBeat(atSeconds, phase, AgentActivityBeatKind.Normal, label, detail);
    }

    /// <summary>
    /// The three states of a tidy map, resolved once for both agent-authored channels: a
    /// tidied string replaces the raw words, an explicit null suppresses the line entirely,
    /// and an absent key — what a tidier outage produces for every line — falls back to the
    /// raw words. Suppression and unavailability answer differently on purpose: collapsing
    /// them would let a failed tidy pass empty the feed instead of coarsening it.
    /// </summary>
    private static string? DisplayTextOf(
        string key,
        string rawText,
        IReadOnlyDictionary<string, string?>? tidiedNarration)
    {
        string? displayText = tidiedNarration is not null && tidiedNarration.TryGetValue(key, out string? tidied)
            ? tidied
            : rawText;

        return string.IsNullOrWhiteSpace(displayText) ? null : displayText;
    }

    private static (string Label, string? Detail) SplitNarration(string flattened)
    {
        if (flattened.Length <= LABEL_MAX_CHARS)
        {
            return (flattened, null);
        }

        int budget = LABEL_MAX_CHARS - TRUNCATION_ELLIPSIS.Length;
        int cut = flattened.LastIndexOf(' ', budget);
        if (cut < LABEL_MAX_CHARS / 2)
        {
            cut = budget;
        }

        string label = flattened[..cut].TrimEnd() + TRUNCATION_ELLIPSIS;
        string detail = Cap(TRUNCATION_ELLIPSIS + flattened[cut..].TrimStart(), DETAIL_MAX_CHARS);
        return (label, detail);
    }

    private static AgentActivityBeat ClassifiedBeat(ToolFact tool, DateTimeOffset? origin, int phase)
    {
        int atSeconds = OffsetSeconds(tool.At, origin);
        (string label, AgentActivityBeatKind kind) = tool.Kind switch
        {
            ActivityKind.LoadSkill => (AgentActivityMapping.LOADED_SKILL_LABEL, AgentActivityBeatKind.Normal),
            ActivityKind.CheckDocumentSet =>
                (AgentActivityMapping.CHECKED_DOCUMENT_SET_LABEL, AgentActivityBeatKind.Normal),
            ActivityKind.Convert => (AgentActivityMapping.CONVERTED_DOCUMENTS_LABEL, AgentActivityBeatKind.Normal),
            ActivityKind.Search => (AgentActivityMapping.SEARCHED_DOCUMENTS_LABEL, AgentActivityBeatKind.Normal),
            ActivityKind.ViewText => (AgentActivityMapping.READ_CONVERTED_TEXT_LABEL, AgentActivityBeatKind.Normal),
            ActivityKind.ViewPage => (AgentActivityMapping.VIEWED_PAGE_LABEL, AgentActivityBeatKind.Hero),
            ActivityKind.WriteWorkingFile => (AgentActivityMapping.RECORDED_WORKING_FILE_LABEL, AgentActivityBeatKind.Normal),
            ActivityKind.RunCode => (AgentActivityMapping.WROTE_AND_RAN_CODE_LABEL, AgentActivityBeatKind.Hero),
            ActivityKind.ComposeAnswer => (AgentActivityMapping.COMPOSED_ANSWER_LABEL, AgentActivityBeatKind.Normal),
            _ => (string.Empty, AgentActivityBeatKind.Normal),
        };

        return new AgentActivityBeat(atSeconds, phase, kind, label, DetailFor(tool))
        {
            DocumentNames = tool.DocumentNames,
        };
    }

    private static string? DetailFor(ToolFact tool)
    {
        List<string> parts = [];
        if (tool.SkillName is string skillName)
        {
            parts.Add(skillName);
        }

        if (tool.Kind == ActivityKind.ViewPage && tool.Pages is string pages)
        {
            parts.Add($"{AgentActivityMapping.PAGE_DETAIL_PREFIX} {pages}");
        }

        if (tool.Kind is ActivityKind.Convert or ActivityKind.ViewPage)
        {
            parts.AddRange(tool.DocumentNames);
        }

        return parts.Count == 0 ? null : Cap(string.Join(DETAIL_SEPARATOR, parts), DETAIL_MAX_CHARS);
    }

    private static int AdvancePhase(int phase, ActivityKind kind)
    {
        int floor = kind switch
        {
            ActivityKind.Convert or ActivityKind.Search or ActivityKind.ViewText =>
                AgentActivityMapping.PHASE_READING_DOCUMENTS,
            ActivityKind.ViewPage => AgentActivityMapping.PHASE_VERIFYING_VISUALLY,
            ActivityKind.WriteWorkingFile => AgentActivityMapping.PHASE_RECORDING_FINDINGS,
            ActivityKind.ComposeAnswer => AgentActivityMapping.PHASE_COMPOSING_ANSWER,
            _ => AgentActivityMapping.PHASE_GETTING_READY,
        };
        return Math.Max(phase, floor);
    }

    private static int OffsetSeconds(DateTimeOffset at, DateTimeOffset? origin)
    {
        return origin is DateTimeOffset start
            ? Math.Max(0, (int)Math.Round((at - start).TotalSeconds))
            : 0;
    }

    private static string Flatten(string text)
    {
        StringBuilder flattened = new(text.Length);
        bool previousWasSpace = false;
        foreach (char character in text)
        {
            char normalized = char.IsControl(character) || char.IsWhiteSpace(character) ? ' ' : character;
            if (normalized == ' ' && previousWasSpace)
            {
                continue;
            }

            previousWasSpace = normalized == ' ';
            _ = flattened.Append(normalized);
        }

        return flattened.ToString().Trim();
    }

    private static string Cap(string text, int maxChars)
    {
        string flattened = Flatten(text);
        if (flattened.Length <= maxChars)
        {
            return flattened;
        }

        int budget = maxChars - TRUNCATION_ELLIPSIS.Length;
        int cut = flattened.LastIndexOf(' ', budget);
        if (cut < maxChars / 2)
        {
            cut = budget;
        }

        return flattened[..cut].TrimEnd() + TRUNCATION_ELLIPSIS;
    }

    private static JsonObject? TryParseInput(string inputTruncated)
    {
        try
        {
            return JsonNode.Parse(inputTruncated) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string Unescape(string salvaged)
    {
        return salvaged
            .Replace("\\n", " ")
            .Replace("\\t", " ")
            .Replace("\\\"", "\"")
            .Replace("\\\\", "\\");
    }

    private static string? NullIfEmpty(string text)
    {
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string? StringOf(JsonObject? record, string property)
    {
        return record?[property] is JsonNode node
            && node.GetValueKind() == System.Text.Json.JsonValueKind.String
            ? node.GetValue<string>()
            : null;
    }

    private static DateTimeOffset? TimestampOf(JsonObject record)
    {
        return StringOf(record, "at") is string at
            && DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed)
            ? parsed
            : null;
    }
}

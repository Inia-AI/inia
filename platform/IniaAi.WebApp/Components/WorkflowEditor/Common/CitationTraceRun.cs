using IniaAi.Common.Numerics;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.Citations.Traces;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using System.Runtime.CompilerServices;

namespace IniaAi.WebApp.Components.WorkflowEditor.Common;

/// <summary>
/// A source as the panel names it. A page source carries its page id, and the span its excerpt
/// covers when the run holds the page's text and the span fits in it.
/// </summary>
public sealed record CitationTraceSourceView(
    string Label,
    string? Excerpt,
    string? PageId = null,
    Range? Span = null,
    string? Url = null,
    Block? Block = null);

public sealed record CitationTraceFactRow(string Figure, string How, string? Detail, IReadOnlyList<CitationTraceSourceView> Sources);

public sealed record CitationTraceRejectionRow(string Figure, IReadOnlyList<string> Reasons, IReadOnlyList<CitationTraceSourceView> Where);

public sealed record CitationTraceReportRow(string Marker, string Figure, IReadOnlyList<CitationTraceSourceView> Sources, string Settled, string Placed);

/// <summary>One block's trace in plain words. <see cref="Report"/> is null when the block built no report paragraph.</summary>
public sealed record CitationTraceBlockView(
    IReadOnlyList<string> Chips,
    IReadOnlyList<CitationTraceFactRow> Facts,
    IReadOnlyList<CitationTraceRejectionRow> TurnedDown,
    IReadOnlyList<CitationTraceReportRow>? Report,
    IReadOnlyList<CitationTraceRejectionRow> ReportTurnedDown);

public sealed record CitationTraceFollowedBlock(Block Block, string Name, IReadOnlyList<string> Lines);

/// <summary>
/// The citation traces of one run as the editor's Citation trace panel reads them. A trace names
/// other blocks by the ids they had in the run and pages by id, so the panel resolves both here:
/// blocks through <see cref="Workflow.TracedBlockIds"/> when an uploaded run was cloned onto new
/// ids, and page text from the pages an uploaded run carries or the pages an editor run loaded.
/// </summary>
public sealed class CitationTraceRun
{
    private const int MAX_PARTIAL_MATCH_LENGTH = 40;
    private const int MAX_ORIGIN_HOPS = 16;
    private const string EARLIER_BLOCK = "an earlier block";

    /// <summary>A run's traces come to a megabyte, and the panel reads the whole run on each search.</summary>
    private static readonly ConditionalWeakTable<string, CitationTraceContent> _parsed = [];

    private readonly List<Block> _blocksInRunOrder;
    private readonly Dictionary<string, Block> _blocksByRunId;
    private readonly Dictionary<string, string> _pageTexts;
    private readonly Dictionary<string, string> _normalized = new(StringComparer.Ordinal);

    private CitationTraceRun(List<Block> blocksInRunOrder, Dictionary<string, Block> blocksByRunId, Dictionary<string, string> pageTexts)
    {
        _blocksInRunOrder = blocksInRunOrder;
        _blocksByRunId = blocksByRunId;
        _pageTexts = pageTexts;
    }

    /// <summary>False for a run made before the platform recorded citation traces.</summary>
    public static bool RecordsTraces(Workflow? workflow)
    {
        return workflow?.GetAllBlocks().Any(static block => block.CitationTrace is not null) ?? false;
    }

    public static CitationTraceRun Of(Workflow? workflow, IEnumerable<Page>? pages = null)
    {
        List<Block> blocks = workflow is null
            ? []
            : [.. workflow.Sections
                .OrderBy(static section => section.Order)
                .SelectMany(static section => section.Blocks.OrderBy(static block => block.Order))];

        Dictionary<string, Block> blocksByRunId = blocks
            .DistinctBy(static block => block.Id)
            .ToDictionary(static block => block.Id, StringComparer.Ordinal);
        foreach ((string runId, string blockId) in workflow?.TracedBlockIds ?? [])
        {
            if (blocksByRunId.TryGetValue(blockId, out Block? block))
            {
                blocksByRunId.TryAdd(runId, block);
            }
        }

        Dictionary<string, string> pageTexts = new(StringComparer.Ordinal);
        foreach (CitationTracePage page in workflow?.CitationTracePages ?? [])
        {
            pageTexts.TryAdd(page.Id, page.Text);
        }

        foreach (Page page in pages ?? [])
        {
            if (page.Text is string text)
            {
                pageTexts.TryAdd(page.Id, text);
            }
        }

        return new CitationTraceRun(blocks, blocksByRunId, pageTexts);
    }

    public string? PageText(string pageId)
    {
        return _pageTexts.GetValueOrDefault(pageId);
    }

    public CitationTraceBlockView? ViewOf(Block block)
    {
        if (ContentOf(block) is not CitationTraceContent content)
        {
            return null;
        }

        return new CitationTraceBlockView(
            Chips(content),
            [.. content.Facts.Select(fact => FactRow(fact, content))],
            RejectionRows(content.Rejections, content),
            content.Report?.Citations.Select(citation => ReportRow(citation, content)).ToList(),
            RejectionRows(content.Report?.Rejections ?? [], content));
    }

    /// <summary>
    /// Every block whose trace states <paramref name="figure"/>, in run order, with what the block
    /// did with it. A figure matches as written, ignoring case, dashes and spacing; a figure that
    /// holds a digit also matches inside a short value, so "55.0" finds "$55.0m".
    /// </summary>
    public IReadOnlyList<CitationTraceFollowedBlock> Follow(string figure)
    {
        string query = Normalize(figure);
        if (query.Length == 0)
        {
            return [];
        }

        bool matchesInside = query.Any(char.IsDigit);
        List<CitationTraceFollowedBlock> followed = [];
        foreach (Block block in _blocksInRunOrder)
        {
            if (ContentOf(block) is not CitationTraceContent content)
            {
                continue;
            }

            List<string> lines = [];
            foreach (TracedFact fact in content.Facts.Where(fact => Matches(fact.Value, query, matchesInside && fact.Numerical)))
            {
                CitationTraceFactRow row = FactRow(fact, content);
                string to = fact.Entry is not TracedFactEntry.Producer && fact.Origin is null && row.Sources.Count > 0
                    ? $", to {Labels(row.Sources)}"
                    : string.Empty;
                lines.Add($"{row.How}{to}{(row.Detail is null ? string.Empty : $" ({row.Detail})")}.");
            }

            foreach (TracedRejection rejection in content.Rejections.Where(rejection => Matches(rejection.Value, query, matchesInside)))
            {
                lines.Add($"Turned down: {LowerFirst(Reason(rejection.Reason))}");
            }

            foreach (TracedCitation citation in content.Report?.Citations.Where(citation => Matches(citation.Value, query, matchesInside)) ?? [])
            {
                lines.Add(citation.Marker is null
                    ? $"In the report: {LowerFirst(Placed(null))}."
                    : $"In the report: marked {LowerFirst(Placed(citation.Marker))}. {Settled(citation.Flattening)}.");
            }

            foreach (TracedRejection rejection in content.Report?.Rejections.Where(rejection => Matches(rejection.Value, query, matchesInside)) ?? [])
            {
                lines.Add($"In the report: {LowerFirst(Reason(rejection.Reason))}");
            }

            if (lines.Count > 0)
            {
                followed.Add(new CitationTraceFollowedBlock(block, NameOf(block), [.. lines.Distinct()]));
            }
        }

        return followed;
    }

    public static string Reason(CitationTraceReason reason)
    {
        return reason switch
        {
            CitationTraceReason.NoSourceStatesValue => "No source of its sentence prints it.",
            CitationTraceReason.SourceDoesNotStateValue => "This source does not print it, so the source was taken off.",
            CitationTraceReason.BareNumberNotInherited => "A number without a unit is not carried from an earlier block.",
            CitationTraceReason.YearContextDiffers => "The source of the earlier year speaks of other things than this sentence.",
            CitationTraceReason.OtherMeaningChosen => "Another fact with the same figure fits the sentence better.",
            CitationTraceReason.SameValueDifferentMeanings => "Earlier blocks state this figure with different meanings, and the sentence does not say which.",
            CitationTraceReason.InputExcludedFromCitations => "This input block is not a citation source, so no figure is cited to it.",
            CitationTraceReason.NoProducerStatesFigure => "No input block states it either: this block worked it out.",
            CitationTraceReason.UndecidedMeaning => "Its meaning is undecided between earlier facts, so it is cited to no block.",
            CitationTraceReason.InsideHtmlTag => "This occurrence sits inside an HTML tag, so it gets no marker.",
            CitationTraceReason.ListItemNumber => "A list item's number is not a figure, so it gets no marker.",
            CitationTraceReason.NegatedByMinus => "This occurrence follows a minus sign the figure does not carry.",
            CitationTraceReason.NoMarkerPlaced => "No occurrence of the figure got a marker, so the citation was dropped.",
            CitationTraceReason.TiedPagesKeptBlockFact => "Pages state this figure with meanings that tie, so a fact that cites only blocks was kept instead.",
            _ => reason.ToString(),
        };
    }

    private static CitationTraceContent? ContentOf(Block block)
    {
        if (block.CitationTrace?.Content is not string json)
        {
            return null;
        }

        if (_parsed.TryGetValue(json, out CitationTraceContent? content))
        {
            return content;
        }

        content = CitationTraceContent.Parse(json);
        if (content is not null)
        {
            _parsed.AddOrUpdate(json, content);
        }

        return content;
    }

    private static List<string> Chips(CitationTraceContent content)
    {
        List<string> chips = [Count(content.Facts.Count, "fact", "facts")];
        foreach (IGrouping<TracedFactEntry, TracedFact> entry in content.Facts.GroupBy(static fact => fact.Entry).OrderBy(static group => group.Key))
        {
            int count = entry.Count();
            chips.Add(entry.Key switch
            {
                TracedFactEntry.Cited => $"{count} cited by this block",
                TracedFactEntry.Split => $"{count} taken from sentences it cited",
                TracedFactEntry.Inherited => $"{count} carried from earlier blocks",
                _ => $"{count} cited to the block that worked {(count == 1 ? "it" : "them")} out",
            });
        }

        if (content.Report is TracedReport report)
        {
            chips.Add(Count(report.Citations.Count(static citation => citation.Marker is not null), "citation marked", "citations marked"));
        }

        chips.Add($"{content.Rejections.Count + (content.Report?.Rejections.Count ?? 0)} turned down");
        return chips;
    }

    private CitationTraceFactRow FactRow(TracedFact fact, CitationTraceContent content)
    {
        List<string> details = [];
        if (fact is { Entry: TracedFactEntry.Producer, SharedWords: int sharedWords })
        {
            details.Add($"{sharedWords} words in common");
        }

        if (fact.ExpandedFrom.Count > 0)
        {
            details.Add($"Gained the pages of {string.Join(", ", fact.ExpandedFrom.Select(origin => BlockName(origin.BlockId)).Distinct())}");
        }

        List<CitationTraceSourceView> sources = fact.Origin is TracedFactOrigin origin ? SourcesOfOrigin(origin) : SourcesOf(fact.Sources, content);
        string how = fact.Entry switch
        {
            TracedFactEntry.Inherited => $"Carried from {(fact.Origin is TracedFactOrigin from ? BlockName(from.BlockId) : EARLIER_BLOCK)}",
            TracedFactEntry.Split => "Inside a sentence this block cited",
            TracedFactEntry.Producer => $"Worked out by {Labels(sources)}",
            _ => "Cited by this block",
        };

        return new CitationTraceFactRow(fact.Value, how, details.Count == 0 ? null : string.Join(" · ", details), sources);
    }

    private List<CitationTraceRejectionRow> RejectionRows(List<TracedRejection> rejections, CitationTraceContent content)
    {
        return [.. rejections
            .GroupBy(static rejection => rejection.Value, StringComparer.Ordinal)
            .Select(group => new CitationTraceRejectionRow(
                group.Key,
                [.. group.Select(static rejection => Reason(rejection.Reason)).Distinct()],
                [.. group.SelectMany(rejection => Where(rejection, content)).Distinct()]))];
    }

    private IEnumerable<CitationTraceSourceView> Where(TracedRejection rejection, CitationTraceContent content)
    {
        if (rejection.Fact is int fact && content.Facts.ElementAtOrDefault(fact) is TracedFact sentence)
        {
            yield return new CitationTraceSourceView(string.Empty, sentence.Value);
        }

        if (rejection.Source is int source && content.Sources.ElementAtOrDefault(source) is TracedSource traced)
        {
            yield return SourceView(traced);
        }

        foreach (TracedFactOrigin origin in rejection.Upstream)
        {
            yield return new CitationTraceSourceView(BlockName(origin.BlockId), Resolve(origin)?.Fact.Value, Block: BlockOf(origin.BlockId));
        }

        if (rejection.BlockId is string blockId)
        {
            yield return new CitationTraceSourceView(BlockName(blockId), null, Block: BlockOf(blockId));
        }
    }

    private CitationTraceReportRow ReportRow(TracedCitation citation, CitationTraceContent content)
    {
        return new CitationTraceReportRow(
            citation.Id,
            citation.Value,
            SourcesOf(citation.Sources, content),
            Settled(citation.Flattening),
            Placed(citation.Marker));
    }

    private static string Settled(TracedFlattening flattening)
    {
        return flattening switch
        {
            TracedFlattening.Direct => "Its own pages and links",
            TracedFlattening.FromUpstream => "The pages and links behind the blocks it cited",
            _ => "No page prints it, so the block stays",
        };
    }

    private static string Placed(TracedMarker? marker)
    {
        return marker switch
        {
            TracedMarker.Literal => "As written",
            TracedMarker.Quantity => "Where the same amount is written another way",
            TracedMarker.Date => "Where the same date is written another way",
            _ => "Not placed, so the citation was dropped",
        };
    }

    private List<CitationTraceSourceView> SourcesOf(List<int> indexes, CitationTraceContent content)
    {
        return [.. indexes
            .Select(index => content.Sources.ElementAtOrDefault(index))
            .OfType<TracedSource>()
            .Select(SourceView)];
    }

    /// <summary>An inherited fact repeats no sources: they belong to the block that cited it first.</summary>
    private List<CitationTraceSourceView> SourcesOfOrigin(TracedFactOrigin origin)
    {
        for (int hop = 0; hop < MAX_ORIGIN_HOPS; hop++)
        {
            if (Resolve(origin) is not (TracedFact fact, CitationTraceContent content))
            {
                return [];
            }

            if (fact.Origin is not TracedFactOrigin next)
            {
                return SourcesOf(fact.Sources, content);
            }

            origin = next;
        }

        return [];
    }

    private (TracedFact Fact, CitationTraceContent Content)? Resolve(TracedFactOrigin origin)
    {
        return BlockOf(origin.BlockId) is Block block
            && ContentOf(block) is CitationTraceContent content
            && content.Facts.ElementAtOrDefault(origin.Fact) is TracedFact fact
                ? (fact, content)
                : null;
    }

    private CitationTraceSourceView SourceView(TracedSource source)
    {
        return source.Type switch
        {
            CitationSourceType.Document => PageView(source),
            CitationSourceType.Url => new CitationTraceSourceView(source.Url ?? "a link", source.Text, Url: WebAddress(source.Url)),
            _ => new CitationTraceSourceView(
                BlockOf(source.BlockId) is Block block ? NameOf(block) : source.BlockTitle ?? EARLIER_BLOCK,
                source.Text,
                Block: BlockOf(source.BlockId)),
        };
    }

    private CitationTraceSourceView PageView(TracedSource source)
    {
        string page = source.PageNumber is int number ? $"page {number}" : "a page";
        string label = string.IsNullOrWhiteSpace(source.DocumentName) ? page : $"{page} of {source.DocumentName}";

        Range? span = source is { PageId: string pageId, Start: int start, Length: int length }
            && PageText(pageId) is string text
            && start >= 0
            && length > 0
            && start + length <= text.Length
                ? new Range(start, start + length)
                : null;

        string? excerpt = span is Range found ? PageText(source.PageId!)![found] : source.Text;
        return new CitationTraceSourceView(label, excerpt, source.PageId, span);
    }

    /// <summary>A trace can arrive in an uploaded file, so only an http or https address becomes a link.</summary>
    private static string? WebAddress(string? url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? address) && (address.Scheme == Uri.UriSchemeHttps || address.Scheme == Uri.UriSchemeHttp)
            ? url
            : null;
    }

    private Block? BlockOf(string? runBlockId)
    {
        return runBlockId is null ? null : _blocksByRunId.GetValueOrDefault(runBlockId);
    }

    private string BlockName(string runBlockId)
    {
        return BlockOf(runBlockId) is Block block ? NameOf(block) : EARLIER_BLOCK;
    }

    private static string NameOf(Block block)
    {
        return !string.IsNullOrWhiteSpace(block.Name) ? block.Name
            : !string.IsNullOrWhiteSpace(block.ReplacementTag) ? block.ReplacementTag
            : EARLIER_BLOCK;
    }

    private static string Labels(IEnumerable<CitationTraceSourceView> sources)
    {
        return string.Join(", ", sources.Select(static source => source.Label).Where(static label => label.Length > 0).Distinct());
    }

    private static string Count(int count, string one, string many)
    {
        return $"{count} {(count == 1 ? one : many)}";
    }

    private bool Matches(string value, string query, bool inside)
    {
        if (!_normalized.TryGetValue(value, out string? normalized))
        {
            normalized = Normalize(value);
            _normalized[value] = normalized;
        }

        return normalized == query
            || (inside && normalized.Length <= MAX_PARTIAL_MATCH_LENGTH && normalized.Contains(query, StringComparison.Ordinal));
    }

    private static string Normalize(string text)
    {
        return string.Join(' ', NumericSanitizer.NormaliseDashes(text)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
    }

    private static string LowerFirst(string text)
    {
        return text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];
    }
}

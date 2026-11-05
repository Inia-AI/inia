using System.Collections.Concurrent;
using System.Globalization;
using IniaAi.Common;
using IniaAi.Core.Workflows.Annotations.Quantities;
using IniaAi.Domain.Entities.Citations;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.Citations.Traces;

namespace IniaAi.Core.Workflows.Annotations;

/// <summary>
/// Records, for one block, how each of its facts came to be and every figure or source a
/// citation step turned down, and writes it as a <see cref="CitationTraceContent"/> when the
/// block's annotations are done. Facts are tracked by object, not by value, because two facts of
/// one value can cite different pages.
/// </summary>
public sealed class CitationTraceRecorder
{
    private readonly Dictionary<Fact, Fact> _parentOf = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Fact, int> _sharedWordsOf = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Fact, List<Fact>> _expandedFrom = new(ReferenceEqualityComparer.Instance);
    private readonly List<PendingRejection> _rejections = [];
    private readonly Dictionary<(CitationTraceReason, string), PendingRejection> _rejectionsByValue = [];
    private readonly List<(TracedCitation Citation, List<BaseSource> Sources)> _citations = [];
    private readonly Dictionary<string, TracedMarker> _markers = [];
    private readonly List<TracedRejection> _reportRejections = [];
    private readonly HashSet<(CitationTraceReason, string)> _reportRejectionKeys = [];
    private SourceRegistry? _sources;

    /// <summary>The trace as last written by <see cref="Finish"/>, with the report's decisions once they are made.</summary>
    public CitationTraceContent? Content { get; private set; }

    internal void Split(Fact parent, Fact part)
    {
        _parentOf[part] = parent;
    }

    internal void Producer(Fact figure, int sharedWords)
    {
        _sharedWordsOf[figure] = sharedWords;
    }

    internal void Expanded(Fact fact, Fact referenced)
    {
        if (!_expandedFrom.TryGetValue(fact, out List<Fact>? upstream))
        {
            upstream = [];
            _expandedFrom[fact] = upstream;
        }

        if (!upstream.Contains(referenced, ReferenceEqualityComparer.Instance))
        {
            upstream.Add(referenced);
        }
    }

    internal void Reject(
        CitationTraceReason reason,
        string value,
        Fact? fact = null,
        BaseSource? source = null,
        string? blockId = null)
    {
        _rejections.Add(new PendingRejection(reason, value, fact, source, [], blockId));
    }

    /// <summary>
    /// Rejects earlier blocks' facts, gathered by value: one entry names every fact of that value
    /// the reason turned down.
    /// </summary>
    internal void RejectUpstream(CitationTraceReason reason, string value, IEnumerable<Fact> upstream)
    {
        if (!_rejectionsByValue.TryGetValue((reason, value), out PendingRejection? pending))
        {
            pending = new PendingRejection(reason, value, null, null, [], null);
            _rejectionsByValue[(reason, value)] = pending;
            _rejections.Add(pending);
        }

        foreach (Fact fact in upstream)
        {
            if (!pending.Upstream.Contains(fact, ReferenceEqualityComparer.Instance))
            {
                pending.Upstream.Add(fact);
            }
        }
    }

    internal void Citation(Citation citation, string value, TracedFlattening flattening)
    {
        _citations.Add((
            new TracedCitation
            {
                Id = citation.Id,
                Value = value,
                Flattening = flattening,
                Matching = citation.Sources.OfType<BlockSource>().Sum(block => block.MatchingSources.Count),
            },
            [.. citation.Sources]));
    }

    internal void Marker(string citationId, TracedMarker marker)
    {
        _ = _markers.TryAdd(citationId, marker);
    }

    /// <summary>A citation dropped from the report because no occurrence of its figure got a marker.</summary>
    internal void Orphan(string citationId)
    {
        if (_citations.FirstOrDefault(pending => pending.Citation.Id == citationId).Citation is TracedCitation citation)
        {
            RejectInReport(CitationTraceReason.NoMarkerPlaced, citation.Value);
        }
    }

    internal void RejectInReport(CitationTraceReason reason, string value)
    {
        if (_reportRejectionKeys.Add((reason, value)))
        {
            _reportRejections.Add(new TracedRejection { Reason = reason, Value = value });
        }
    }

    /// <summary>
    /// Writes the trace of <paramref name="annotations"/> as they stand and registers each of the
    /// block's own facts in <paramref name="origins"/>, so a later block that inherits one names it.
    /// </summary>
    internal CitationTraceContent Finish(
        BlockAnnotations annotations,
        PageIndex pages,
        FactOrigins origins,
        string blockId)
    {
        var sources = new SourceRegistry(pages);
        _sources = sources;
        var indexOf = new Dictionary<Fact, int>(ReferenceEqualityComparer.Instance);
        for (int index = 0; index < annotations.Facts.Count; index++)
        {
            indexOf[annotations.Facts[index]] = index;
        }

        List<TracedFact> facts = [.. annotations.Facts.Select(fact => Describe(fact, annotations, indexOf, sources, origins))];

        for (int index = 0; index < annotations.Facts.Count; index++)
        {
            Fact fact = annotations.Facts[index];
            if (!annotations.IsReferenced(fact) || origins.OriginOf(fact) is null)
            {
                origins.Register(fact, new TracedFactOrigin(blockId, index));
            }
        }

        Content = new CitationTraceContent
        {
            Facts = facts,
            Rejections = [.. _rejections.Select(rejection => new TracedRejection
            {
                Reason = rejection.Reason,
                Value = Cut(rejection.Value, CitationTraceLimits.MAX_VALUE_LENGTH),
                Fact = rejection.Fact is not null && indexOf.TryGetValue(rejection.Fact, out int factIndex) ? factIndex : null,
                Source = rejection.Source is null ? null : sources.IndexOf(rejection.Source),
                Upstream = [.. rejection.Upstream.Select(origins.OriginOf).OfType<TracedFactOrigin>()],
                BlockId = rejection.BlockId,
            })],
            Sources = sources.Traced,
        };

        return Content;
    }

    /// <summary>The finished trace with the decisions the block's report paragraph made.</summary>
    internal CitationTraceContent? FinishReport()
    {
        if (Content is null || _sources is null)
        {
            return null;
        }

        Content.Report = new TracedReport
        {
            Citations = [.. _citations.Select(pending => pending.Citation with
            {
                Sources = [.. pending.Sources.Select(_sources.IndexOf)],
                Marker = _markers.TryGetValue(pending.Citation.Id, out TracedMarker marker) ? marker : null,
            })],
            Rejections = [.. _reportRejections],
        };

        return Content;
    }

    private TracedFact Describe(
        Fact fact,
        BlockAnnotations annotations,
        Dictionary<Fact, int> indexOf,
        SourceRegistry sources,
        FactOrigins origins)
    {
        TracedFactOrigin? origin = annotations.IsReferenced(fact) ? origins.OriginOf(fact) : null;
        TracedFactEntry entry = annotations.IsReferenced(fact) ? TracedFactEntry.Inherited
            : _parentOf.ContainsKey(fact) ? TracedFactEntry.Split
            : _sharedWordsOf.ContainsKey(fact) ? TracedFactEntry.Producer
            : TracedFactEntry.Cited;

        return new TracedFact
        {
            Value = Cut(fact.Value, CitationTraceLimits.MAX_VALUE_LENGTH),
            CutFromLength = fact.Value.Length > CitationTraceLimits.MAX_VALUE_LENGTH ? fact.Value.Length : 0,
            Entry = entry,
            Numerical = fact.IsNumerical,
            Quantity = fact.Quantity is null ? null : Describe(fact.Quantity),
            TableCellAtPageScale = fact.IsTableCellAtPageScale,
            Parent = _parentOf.TryGetValue(fact, out Fact? parent) && indexOf.TryGetValue(parent, out int parentIndex) ? parentIndex : null,
            Origin = origin,
            Sources = origin is null ? [.. fact.Sources.Select(sources.IndexOf)] : [],
            ExpandedFrom = _expandedFrom.TryGetValue(fact, out List<Fact>? upstream)
                ? [.. upstream.Select(origins.OriginOf).OfType<TracedFactOrigin>()]
                : [],
            SharedWords = _sharedWordsOf.TryGetValue(fact, out int sharedWords) ? sharedWords : null,
        };
    }

    private static string Describe(Quantity quantity)
    {
        string amount = quantity.BaseValue.ToString("R", CultureInfo.InvariantCulture);
        return quantity.Subtype is null ? $"{quantity.Kind} {amount}" : $"{quantity.Kind} {quantity.Subtype} {amount}";
    }

    private static string Cut(string text, int maxLength)
    {
        return text.Length > maxLength
            ? text.Truncate(maxLength) + BlockAnnotationProcessor.CITED_TEXT_ELLIPSIS
            : text;
    }

    private sealed record PendingRejection(
        CitationTraceReason Reason,
        string Value,
        Fact? Fact,
        BaseSource? Source,
        List<Fact> Upstream,
        string? BlockId);

    /// <summary>
    /// The block's sources, each written once. A page source names its page and where its excerpt
    /// sits in the page's text; only an excerpt the page does not hold is copied, cut short.
    /// </summary>
    private sealed class SourceRegistry(PageIndex pages)
    {
        private readonly Dictionary<BaseSource, int> _byInstance = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<(string Key, string Text), int> _byValue = [];

        public List<TracedSource> Traced { get; } = [];

        public int IndexOf(BaseSource source)
        {
            if (_byInstance.TryGetValue(source, out int index))
            {
                return index;
            }

            if (!_byValue.TryGetValue((source.SourceKey, source.CitedText), out index))
            {
                index = Traced.Count;
                Traced.Add(Describe(source));
                _byValue[(source.SourceKey, source.CitedText)] = index;
            }

            _byInstance[source] = index;
            return index;
        }

        private TracedSource Describe(BaseSource source)
        {
            return source switch
            {
                DocumentSource page => DescribePage(page),
                UrlSource url => new TracedSource { Type = CitationSourceType.Url, Url = url.Url, Text = Excerpt(url.CitedText) },
                BlockSource block => new TracedSource
                {
                    Type = CitationSourceType.Block,
                    BlockId = block.BlockId,
                    BlockTitle = block.BlockTitle,
                    Text = Excerpt(block.CitedText),
                },
                _ => new TracedSource { Type = source.Type, Text = Excerpt(source.CitedText) },
            };
        }

        private TracedSource DescribePage(DocumentSource page)
        {
            (int Start, int Length)? span = pages.PageTextOf(page.PageId) is string text ? Locate(text, page.CitedText) : null;

            return new TracedSource
            {
                Type = CitationSourceType.Document,
                PageId = page.PageId,
                PageNumber = page.PageNumber,
                DocumentName = page.DocumentName,
                Start = span?.Start,
                Length = span?.Length,
                Text = span is null ? Excerpt(page.CitedText) : null,
            };
        }

        private static (int Start, int Length)? Locate(string pageText, string excerpt)
        {
            string core = excerpt.Trim().Trim(BlockAnnotationProcessor.CITED_TEXT_ELLIPSIS[0]).Trim();
            if (core.Length == 0)
            {
                return null;
            }

            int start = pageText.IndexOf(core, StringComparison.Ordinal);
            return start < 0 ? null : (start, core.Length);
        }

        private static string? Excerpt(string text)
        {
            return string.IsNullOrEmpty(text) ? null : Cut(text, CitationTraceLimits.MAX_EXCERPT_LENGTH);
        }
    }
}

/// <summary>
/// The run's facts by object, each with the block that wrote it into its trace, so a block that
/// inherits a fact names it instead of copying its sources.
/// </summary>
public sealed class FactOrigins
{
    private readonly ConcurrentDictionary<Fact, TracedFactOrigin> _origins = new(ReferenceEqualityComparer.Instance);

    internal void Register(Fact fact, TracedFactOrigin origin)
    {
        _origins[fact] = origin;
    }

    internal TracedFactOrigin? OriginOf(Fact fact)
    {
        return _origins.TryGetValue(fact, out TracedFactOrigin? origin) ? origin : null;
    }
}

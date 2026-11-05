using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Common.Text;
using IniaAi.Core.Common.Helpers;
using IniaAi.Core.Workflows.Annotations.Quantities;
using IniaAi.Domain.Entities.Citations;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.Citations.Traces;
using IniaAi.Domain.Entities.Documents;
using System.Text.RegularExpressions;

namespace IniaAi.Core.Workflows.Annotations;

public interface IBlockAnnotationProcessor
{
    void ProcessFacts(
        ResponseContentSequence responseContent,
        Dictionary<int, Page> pages,
        BlockAnnotations blockAnnotations);
    void ProcessConfidenceScore(string rawResponse, BlockAnnotations blockAnnotations);
}

public partial class BlockAnnotationProcessor : IBlockAnnotationProcessor
{
    internal const int MAX_CITED_TEXT_LENGTH = 400;
    internal const string CITED_TEXT_ELLIPSIS = TextPassage.ELLIPSIS;

    private readonly IFactExtractor _factExtractor;
    private readonly IConfidenceScoreExtractor _confidenceScoreExtractor;
    private readonly ISourceExtractor _sourceExtractor;

    public BlockAnnotationProcessor(
        IFactExtractor factExtractor,
        IConfidenceScoreExtractor confidenceScoreExtractor,
        ISourceExtractor sourceExtractor)
    {
        _factExtractor = factExtractor;
        _confidenceScoreExtractor = confidenceScoreExtractor;
        _sourceExtractor = sourceExtractor;
    }

    public void ProcessFacts(
        ResponseContentSequence responseContent,
        Dictionary<int, Page> pages,
        BlockAnnotations blockAnnotations)
    {
        if (!responseContent.Any()
            || pages is null
            || blockAnnotations is null)
        {
            return;
        }

        List<Fact>? collectedFacts = _factExtractor.IdentifyAndCollectFacts(
            responseContent,
            pages);

        if (collectedFacts is null)
        {
            return;
        }

        blockAnnotations.AddFacts(collectedFacts);
    }

    public static void CreateFactsFromPages(
        Dictionary<int, Page> filteredPages,
        BlockAnnotations blockAnnotations,
        bool shouldUseOverviews)
    {
        List<Fact> collectedFacts = [];

        foreach (Page page in filteredPages.Values)
        {
            string? pageContent = shouldUseOverviews
                ? page.Overview
                : page.Text;

            if (string.IsNullOrWhiteSpace(pageContent))
            {
                continue;
            }

            Fact fact = new(pageContent.Trim());

            if (!string.IsNullOrWhiteSpace(page.SourceUrl))
            {
                fact.Sources.Add(new UrlSource
                {
                    Url = page.SourceUrl,
                    CitedText = pageContent,
                });
            }
            else
            {
                fact.Sources.Add(new DocumentSource
                {
                    CitedText = pageContent,
                    DocumentId = page.DocumentId,
                    PageId = page.Id,
                    PageNumber = page.PageNumber,
                    FilePageNumber = page.FilePageNumber,
                    DocumentName = page.Document?.FileName ?? string.Empty
                });
            }

            collectedFacts.Add(fact);
        }

        blockAnnotations.AddFacts(collectedFacts);
    }

    /// <summary>
    /// Splits each broad fact in <paramref name="annotations"/> into the broad
    /// parent + grounded numerical sub-facts via <see cref="CreateNumericalFacts"/>,
    /// then verifies each numerical sub-fact's sources against their available
    /// text via <see cref="VerifyNumericalFactSources"/>,
    /// then drops numerical sub-facts that lost all sources.
    /// <para>
    /// Runs after <see cref="ExpandSourcesFromReferencedFact"/> has merged
    /// upstream sources onto local broad facts. The grounding inside
    /// <see cref="CreateNumericalFacts"/> distributes the merged source set
    /// to numerical sub-facts (literal substring → multiplier-stripped →
    /// kind-aware quantity equivalence or the same date in another form) in
    /// one pass — no need for a parallel relatedness check at the expansion layer.
    /// </para>
    /// <para>
    /// Iteration order preserves the existing fact order: local broad facts
    /// (added first by ProcessFacts) split before any upstream-injected facts
    /// (added later by AddReferencedFacts). On dedup by Value, locally-split
    /// numerical sub-facts win over upstream-injected duplicates that would
    /// have brought a smaller source set. An upstream-injected fact is the
    /// earlier block's own object, split and verified there, so it passes
    /// through unchanged: other blocks read it while this one runs.
    /// </para>
    /// </summary>
    public static void SplitNumericalFacts(
        BlockAnnotations annotations,
        IReadOnlyList<Page> pages)
    {
        SplitNumericalFacts(annotations, new PageIndex(pages));
    }

    internal static void SplitNumericalFacts(
        BlockAnnotations annotations,
        PageIndex pageIndex)
    {
        List<Fact> snapshot = [.. annotations.Facts];
        annotations.Facts.Clear();

        foreach (Fact fact in snapshot)
        {
            annotations.AddFacts(annotations.IsReferenced(fact) ? [fact] : CreateNumericalFacts(fact, pageIndex, annotations.Trace));
        }

        VerifyNumericalFactSources([.. annotations.Facts.Where(fact => !annotations.IsReferenced(fact))], pageIndex, annotations.Trace);

        // After verification a numerical sub-fact may have lost all its
        // sources — drop it (consistent with CreateNumericalFacts dropping
        // numerical sub-facts that no parent source grounds). Broad facts
        // are kept regardless of source state.
        _ = annotations.Facts.RemoveAll(f => f.IsNumerical && f.Sources.Count == 0);
    }

    /// <summary>
    /// Given a broad fact, extracts numerical sub-facts. If the fact's entire
    /// text IS a single numerical value, marks the original as numerical.
    /// Otherwise, returns the broad fact (non-numerical, with all original
    /// sources) plus numerical sub-facts grounded in those sources whose
    /// CitedText contains the value either literally (Step 1), with the
    /// multiplier suffix stripped (Step 2), or as a kind-aware
    /// quantity-equivalent value or the same date in another form (Step 3 —
    /// handles re-synthesis like "$184 billion" → "$183,990 million" and
    /// "May 2011" → "2011-05-01"). Sub-facts with no grounded
    /// source are dropped — we never attach a numeric fact to a source
    /// whose CitedText doesn't mention it.
    /// </summary>
    internal static List<Fact> CreateNumericalFacts(Fact originalFact, PageIndex? pages = null, CitationTraceRecorder? trace = null)
    {
        List<string> numericalValues = NumericalDataExtractor.Extract(originalFact.Value);

        if (numericalValues.Count == 0)
        {
            // No numerical data — keep as broad fact (non-numerical)
            return [originalFact];
        }

        // If single extracted value equals the full text, mark the original as numerical
        if (numericalValues.Count == 1
            && numericalValues[0] == originalFact.Value)
        {
            originalFact.IsNumerical = true;
            return [originalFact];
        }

        // Multiple values or the extracted value is a subset of the text:
        // Keep the broad fact (non-numerical) and add grounded numerical sub-facts.
        var result = new List<Fact> { originalFact };
        List<Quantity>? signedInWords = null;

        foreach (string value in numericalValues)
        {
            var numericalFact = new Fact(value) { IsNumerical = true };

            // Step 1: exact match — source CitedText contains the extracted value
            foreach (BaseSource source in originalFact.Sources)
            {
                if (source.CitedText.Contains(value, StringComparison.OrdinalIgnoreCase))
                {
                    numericalFact.Sources.Add(source);
                }
            }

            // Each later step runs only while no Document/Url source is grounded.
            // Step 2: the multiplier-suffix-stripped value (e.g. "$183,866 million" ->
            // "$183,866" when CitedText has "$183,866" but not the full suffixed value —
            // common with "(in millions)" column headers).
            // Step 3: kind-aware quantity equivalence — handles near-rounding re-synthesis
            // like "$184 billion" matching a CitedText that contains "$183,990 million"
            // (Currency USD, well within <see cref="QuantityMatcher.ToleranceByKind"/>) and
            // rejects cross-kind collisions ($300B vs 300B Kelvin). A date is grounded the
            // same way by another form of the same date.
            // Step 4: a page printing the figure as a table cell under a scale header:
            // £2.506m is the cell 2,506 under £'000.
            string? coreNumber = FactExtractor.StripMultiplierSuffix(value);
            Quantity? claimed = numericalFact.Quantity;
            Func<BaseSource, bool>[] laterSteps =
            [
                source => coreNumber is not null && source.CitedText.Contains(coreNumber, StringComparison.OrdinalIgnoreCase),
                source => NumericalGrounding.StatesEquivalentValue(source.CitedText, value),
                source => source is DocumentSource page
                    && claimed is not null
                    && pages?.ScaleOf(page) is StatementScale scale
                    && scale.StatesScaled(pages.QuantitiesOf(source.CitedText), claimed),
            ];

            foreach (Func<BaseSource, bool> step in laterSteps)
            {
                if (numericalFact.Sources.Any(s => s is not BlockSource))
                {
                    break;
                }

                foreach (BaseSource source in originalFact.Sources)
                {
                    if (!numericalFact.Sources.Contains(source) && step(source))
                    {
                        numericalFact.Sources.Add(source);
                    }
                }
            }

            // A table cell on a scaled page stands for the amount, so it matches that amount
            // written out downstream rather than staying a bare number nothing inherits.
            if (pages is not null
                && claimed is not null
                && SingleScaleOf(numericalFact, pages) is StatementScale cellScale
                && cellScale.Scale(claimed) is Quantity scaled)
            {
                numericalFact.Quantity = scaled;
                numericalFact.IsTableCellAtPageScale = true;
            }
            else if (SignedInWords(
                originalFact.Value,
                signedInWords ??= [.. UnitExtractor.Extract(originalFact.Value).Where(quantity => quantity.SignStatedInWords)],
                value) is Quantity signed)
            {
                numericalFact.Quantity = signed;
            }

            // Drop sub-facts with no grounded source rather than attach unrelated parent
            // sources — a numeric citation pointing at text that doesn't mention the number
            // is misleading. The broad parent fact still preserves the data.
            if (numericalFact.Sources.Count > 0)
            {
                result.Add(numericalFact);
                trace?.Split(originalFact, numericalFact);
            }
            else
            {
                trace?.Reject(CitationTraceReason.NoSourceStatesValue, value, originalFact);
            }
        }

        return result;
    }

    /// <summary>
    /// The quantity among <paramref name="signedInWords"/>, the ones a word in <paramref name="text"/>
    /// gives a direction to ("declined 42.38%", "a loss of $0.8mn"), whose own span states
    /// <paramref name="value"/>. Such a quantity is compared by magnitude, as a claim with a sign
    /// word is, so a later "-42.38%" can inherit the fact.
    /// </summary>
    private static Quantity? SignedInWords(string text, List<Quantity> signedInWords, string value)
    {
        return signedInWords.FirstOrDefault(quantity =>
            NumericalGrounding.MatchesOnNumberBoundaries(text[quantity.Start..quantity.End], value).Any());
    }

    /// <summary>
    /// The one scale every page the fact cites prints, or null when it cites no page or pages
    /// that disagree.
    /// </summary>
    private static StatementScale? SingleScaleOf(Fact fact, PageIndex pages)
    {
        List<StatementScale?> scales = [.. fact.Sources.OfType<DocumentSource>().Select(pages.ScaleOf).Distinct()];
        return scales.Count == 1 ? scales[0] : null;
    }

    public static void BoundCitationSources(Citation citation, string value)
    {
        citation.Sources = [.. citation.Sources.Select(source => BoundedSource(source, value))];
    }

    internal static BaseSource BoundedSource(BaseSource source, string value)
    {
        string bounded = BoundCitedText(source.CitedText, value);
        return ReferenceEquals(bounded, source.CitedText)
            ? source
            : source.WithCitedText(bounded);
    }

    /// <summary>
    /// The source with its cited text cut to the excerpt limit from the start, for a list that
    /// serves many facts and so has no one value to centre on.
    /// </summary>
    internal static BaseSource HeadBoundedSource(BaseSource source)
    {
        return source.CitedText.Length <= MAX_CITED_TEXT_LENGTH
            ? source
            : source.WithCitedText(Excerpt(source.CitedText, 0));
    }

    private static string BoundCitedText(string citedText, string value)
    {
        if (citedText.Length <= MAX_CITED_TEXT_LENGTH)
        {
            return citedText;
        }

        (int needleIndex, int needleLength) = NumericalGrounding.LocateValue(citedText, value);
        if (needleIndex < 0)
        {
            return citedText;
        }

        int windowStart = Math.Clamp(
            needleIndex + (needleLength / 2) - (MAX_CITED_TEXT_LENGTH / 2),
            0,
            citedText.Length - MAX_CITED_TEXT_LENGTH);

        return Excerpt(citedText, windowStart);
    }

    /// <summary>
    /// The excerpt limit's worth of text from <paramref name="windowStart"/>, never splitting a
    /// surrogate pair, with an ellipsis on each side that was cut.
    /// </summary>
    private static string Excerpt(string citedText, int windowStart)
    {
        int windowEnd = windowStart + MAX_CITED_TEXT_LENGTH;

        if (windowStart > 0 && char.IsLowSurrogate(citedText[windowStart]))
        {
            windowStart++;
        }

        if (windowEnd < citedText.Length && char.IsLowSurrogate(citedText[windowEnd]))
        {
            windowEnd--;
        }

        string window = citedText[windowStart..windowEnd];
        string prefix = windowStart > 0 ? CITED_TEXT_ELLIPSIS : string.Empty;
        string suffix = windowEnd < citedText.Length ? CITED_TEXT_ELLIPSIS : string.Empty;

        return prefix + window + suffix;
    }

    /// <summary>
    /// Drops a numerical fact's sources whose available text does not ground
    /// the value (literal, multiplier-stripped, or kind-aware-equivalent — the
    /// same rule <see cref="CreateNumericalFacts"/> attaches with). The
    /// AI-supplied <c>CitedText</c> is the AI's CLAIM; a
    /// <see cref="DocumentSource"/> whose page is in scope is checked against
    /// the page's real <see cref="Page.Text"/> so a wrong claim can't keep the
    /// source, and against its <c>CitedText</c> only when the page text is
    /// unavailable. A <see cref="UrlSource"/> is checked against its
    /// <c>CitedText</c>. Conservative when no text is available at all
    /// (out-of-scope page with empty CitedText, OCR-pending) — keeps the
    /// source. <see cref="BlockSource"/> aggregates are left to their upstream
    /// verification. Only applies to <see cref="Fact.IsNumerical"/> facts;
    /// broad facts keep all sources.
    /// </summary>
    private static void VerifyNumericalFactSources(
        List<Fact> facts,
        PageIndex pages,
        CitationTraceRecorder trace)
    {
        foreach (Fact fact in facts)
        {
            if (!fact.IsNumerical)
            {
                continue;
            }

            foreach (BaseSource source in fact.Sources.Where(source => IsUngroundedNumericalSource(source, fact, pages)).ToList())
            {
                _ = fact.Sources.Remove(source);
                trace.Reject(CitationTraceReason.SourceDoesNotStateValue, fact.Value, fact, source);
            }
        }
    }

    private static bool IsUngroundedNumericalSource(
        BaseSource source,
        Fact fact,
        PageIndex pages)
    {
        string? groundingText = source switch
        {
            DocumentSource page => pages.TextOf(page),
            UrlSource url => url.CitedText,
            _ => null,
        };

        return !string.IsNullOrEmpty(groundingText)
            && !NumericalGrounding.TextContainsValue(groundingText, fact.Value)
            && !(source is DocumentSource cited
                && fact.Quantity is not null
                && pages.ScaleOf(cited) is StatementScale scale
                && scale.StatesScaled(pages.QuantitiesOf(groundingText), fact.Quantity));
    }

    /// <summary>
    /// A fact is citable when it is numerical and not a bare single digit.
    /// Single-digit values carry no attribution value and, when they fall on
    /// an ordered-list ordinal ("5." → <c>&lt;fact&gt;5&lt;/fact&gt;.</c>),
    /// break the markdown list. Both citation building and marker application
    /// share this gate so citation IDs stay aligned by fact value.
    /// </summary>
    public static bool IsCitableNumericalFact(Fact fact)
    {
        return fact.IsNumerical && !IsBareSingleDigit(fact.Value);
    }

    private static bool IsBareSingleDigit(string value)
    {
        return value.Length == 1 && char.IsAsciiDigit(value[0]);
    }

    /// <summary>
    /// Applies fact markers to content using citation IDs.
    /// Only numerical facts get <fact id="cX">text</fact> markers.
    /// Broad (non-numerical) facts are kept in the citations array but not linked to content.
    /// Three passes:
    ///   Tier 1 — literal substring of fact.Value with word boundaries (regex).
    ///   Tier 2/3 — for facts whose surface form doesn't appear literally,
    ///              extract quantities from the response and wrap any span whose
    ///              quantity is kind-aware-equivalent to the fact's quantity.
    ///              Skips spans the Tier 1 pass already wrapped.
    ///   Date pass — for unmatched date-valued facts (which carry no quantity),
    ///              wrap the first date span that states the same calendar date
    ///              in another form. Skips spans either earlier pass wrapped.
    /// </summary>
    public static string ApplyFactsWithIds(
        string rawResponse,
        IReadOnlyList<Fact> facts,
        IReadOnlyList<Citation> citations,
        CitationTraceRecorder? trace = null)
    {
        if (string.IsNullOrWhiteSpace(rawResponse) || facts.Count == 0 || citations.Count == 0)
        {
            return rawResponse;
        }

        return FactMarkers.Prepare(facts, citations)?.Apply(rawResponse, trace) ?? rawResponse;
    }

    /// <summary>
    /// The fact markers of one set of facts and citations, prepared once and applied to any number
    /// of texts, as <see cref="ApplyFactsWithIds"/> applies them to one. A data output applies them
    /// to each value of its JSON, so the literal pattern is compiled once, not once per value.
    /// </summary>
    public sealed class FactMarkers
    {
        private readonly IReadOnlyList<Fact> _facts;
        private readonly Dictionary<string, string> _factToCitationId;
        private readonly Dictionary<string, string> _factDictionary;
        private readonly Regex? _literalPattern;

        private FactMarkers(
            IReadOnlyList<Fact> facts,
            Dictionary<string, string> factToCitationId,
            Dictionary<string, string> factDictionary,
            Regex? literalPattern)
        {
            _facts = facts;
            _factToCitationId = factToCitationId;
            _factDictionary = factDictionary;
            _literalPattern = literalPattern;
        }

        /// <summary>Null when no citable numerical fact has a citation, so no text gets a marker.</summary>
        public static FactMarkers? Prepare(IReadOnlyList<Fact> facts, IReadOnlyList<Citation> citations)
        {
            // Build mapping from fact value to citation ID.
            // Only numerical facts get citations (broad facts are skipped in BuildCitations),
            // so we must only include numerical facts here to keep index alignment.
            var factToCitationId = new Dictionary<string, string>();
            var uniqueFactValues = new List<string>();
            foreach (Fact fact in facts)
            {
                if (!IsCitableNumericalFact(fact))
                {
                    continue;
                }

                if (!uniqueFactValues.Contains(fact.Value))
                {
                    uniqueFactValues.Add(fact.Value);
                }
            }

            for (int i = 0; i < uniqueFactValues.Count && i < citations.Count; i++)
            {
                factToCitationId[uniqueFactValues[i]] = citations[i].Id;
            }

            // Build replacement dictionary with citation IDs — only for numerical facts
            var factDictionary = new Dictionary<string, string>();
            foreach (Fact fact in facts)
            {
                if (!IsCitableNumericalFact(fact) || fact.IsTableCellAtPageScale)
                {
                    continue;
                }

                if (factDictionary.ContainsKey(fact.Value))
                {
                    continue;
                }

                if (factToCitationId.TryGetValue(fact.Value, out string? citationId))
                {
                    factDictionary.Add(fact.Value, $"<fact id=\"{citationId}\">{fact.Value}</fact>");
                }
                else
                {
                    // Fallback to simple fact tag
                    factDictionary.Add(fact.Value, $"<fact>{fact.Value}</fact>");
                }
            }

            if (factToCitationId.Count == 0)
            {
                return null;
            }

            // Sort keys by length (longest first) so multi-token facts win over their
            // substrings in regex alternation. Without this, a fact like "31" would
            // match the day inside "31 December 2024" before the longer date fact got a
            // chance, splitting one annotated date into a stray standalone-day citation.
            IOrderedEnumerable<string> sortedKeys = factDictionary.Keys.OrderByDescending(k => k.Length);

            // Build regex pattern for all fact values with word boundaries
            // Word boundaries prevent partial matches (e.g., "5.1%" matching within "95.1%").
            // A table cell read at its page's scale has no literal pass, so the pattern can be empty.
            string pattern = string.Join("|", sortedKeys.Select(k => NumericalGrounding.WrapWithWordBoundaries(Regex.Escape(k), k)));
            return new FactMarkers(
                facts,
                factToCitationId,
                factDictionary,
                pattern.Length == 0 ? null : new Regex(pattern, RegexOptions.Compiled));
        }

        public string Apply(string rawResponse, CitationTraceRecorder? trace = null)
        {
            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                return rawResponse;
            }

            IReadOnlyList<Fact> facts = _facts;
            Dictionary<string, string> factToCitationId = _factToCitationId;
            Dictionary<string, string> factDictionary = _factDictionary;
            IEnumerable<Match> literalMatches = _literalPattern is null
                ? []
                : _literalPattern.Matches(rawResponse);

            // HTML tag regions: a fact marker must never be emitted inside a tag or
            // attribute, because the emitted <fact id="..."> quote prematurely
            // closes the enclosing attribute and corrupts the markup (the
            // merge-block HTML-table corruption). Any match or quantity span that
            // overlaps a tag region is skipped; body-text occurrences still wrap.
            // A list item's number ("10." or "12)" opening a line) numbers the list
            // and states no figure, so it is never wrapped either.
            List<(int Start, int End)> htmlTagRegions = ComputeHtmlTagRegions(rawResponse);
            List<(int Start, int End)> listItemNumberRegions = ComputeListItemNumberRegions(rawResponse);
            IReadOnlyList<(int Start, int End)> unwrappableRegions = [.. htmlTagRegions, .. listItemNumberRegions];

            // Tier 1: collect substring matches and their positions.
            var spans = new List<MarkerSpan>();
            var consumedValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in literalMatches)
            {
                if (!factDictionary.TryGetValue(match.Value, out string? wrapped))
                {
                    continue;
                }

                CitationTraceReason? skipped =
                    OverlapsAnyRegion(match.Index, match.Index + match.Length, htmlTagRegions) ? CitationTraceReason.InsideHtmlTag
                    : OverlapsAnyRegion(match.Index, match.Index + match.Length, listItemNumberRegions) ? CitationTraceReason.ListItemNumber
                    : NumericalGrounding.IsNegatedByAMinus(rawResponse, match.Index) && NumericalGrounding.KeepsItsSign(match.Value) ? CitationTraceReason.NegatedByMinus
                    : null;

                if (skipped is CitationTraceReason reason)
                {
                    trace?.RejectInReport(reason, match.Value);
                    continue;
                }

                spans.Add(new MarkerSpan(match.Index, match.Index + match.Length, wrapped, factToCitationId.GetValueOrDefault(match.Value), TracedMarker.Literal));
                _ = consumedValues.Add(match.Value);
            }

            // Tier 2/3: for unmatched numerical facts with Quantity, look for a
            // kind-aware quantity-equivalent span elsewhere in rawResponse. Skips
            // spans already claimed by Tier 1 — the higher-confidence pass owns
            // them. Dedupe unmatched facts by Value so a single citation slot
            // doesn't claim two spans.
            List<Fact> unmatched = [.. facts
                .Where(f => IsCitableNumericalFact(f)
                    && f.Quantity is not null
                    && !consumedValues.Contains(f.Value))
                .GroupBy(f => f.Value)
                .Select(g => g.First())];
            var claimedSpans = new HashSet<(int Start, int End)>(spans.Select(s => (s.Start, s.End)));

            if (unmatched.Count > 0)
            {
                // Drop quantities that fall inside an HTML tag region so the
                // fallback pass can never wrap inside a tag / attribute either.
                IReadOnlyList<Quantity> rawQuantities = [.. UnitExtractor.Extract(rawResponse)
                    .Where(q => !OverlapsAnyRegion(q.Start, q.End, unwrappableRegions))];

                foreach (Fact fact in unmatched)
                {
                    // A Plain-kinded fact is too ambiguous to claim a typed
                    // haystack span (e.g. fact "100" wrapping "100%"). Tier 3
                    // wildcard exists to recover TYPED facts whose haystack form
                    // dropped its unit, not the reverse direction.
                    if (fact.Quantity!.Kind is UnitKind.Plain)
                    {
                        QuantityMatch? plainMatch = FindPlainOnlyMatch(
                            fact.Quantity, rawQuantities, claimedSpans);
                        if (plainMatch is null)
                        {
                            continue;
                        }

                        AddFallbackSpan(plainMatch, fact, rawQuantities, factToCitationId, spans, claimedSpans);
                        continue;
                    }

                    QuantityMatch? match = QuantityMatcher.FindBestMatch(
                        fact.Quantity, rawQuantities, claimedSpans);
                    if (match is null)
                    {
                        continue;
                    }

                    AddFallbackSpan(match, fact, rawQuantities, factToCitationId, spans, claimedSpans);
                }
            }

            AddRestatedDateSpans(rawResponse, facts, consumedValues, factToCitationId, unwrappableRegions, spans, claimedSpans);

            if (spans.Count == 0)
            {
                return rawResponse;
            }

            // Emit: walk original in span order, interleaving wrapped markers.
            spans.Sort((a, b) => a.Start.CompareTo(b.Start));
            var sb = new System.Text.StringBuilder(rawResponse.Length + (32 * spans.Count));
            int cursor = 0;
            foreach (MarkerSpan span in spans)
            {
                if (span.Start < cursor)
                {
                    continue;
                }

                _ = sb.Append(rawResponse, cursor, span.Start - cursor);
                _ = sb.Append(span.Wrapped);
                cursor = span.End;

                if (span.CitationId is not null)
                {
                    trace?.Marker(span.CitationId, span.Marker);
                }
            }

            _ = sb.Append(rawResponse, cursor, rawResponse.Length - cursor);
            return sb.ToString();
        }
    }

    private sealed record MarkerSpan(int Start, int End, string Wrapped, string? CitationId, TracedMarker Marker);

    /// <summary>
    /// Locates every HTML tag (open, close, or self-closing) in the text and
    /// returns their [Start, End) character ranges. The strict opener
    /// (<c>&lt;</c> followed by a letter, <c>/</c>, or <c>!</c>) means prose
    /// like <c>x &lt; 5</c> is not mistaken for a tag.
    /// </summary>
    internal static List<(int Start, int End)> ComputeHtmlTagRegions(string text)
    {
        return RegionsOf(HtmlTagRegex(), text);
    }

    private static List<(int Start, int End)> ComputeListItemNumberRegions(string text)
    {
        return RegionsOf(ListItemNumberRegex(), text);
    }

    private static List<(int Start, int End)> RegionsOf(Regex regex, string text)
    {
        return [.. regex.Matches(text).Select(match => (match.Index, match.Index + match.Length))];
    }

    internal static bool OverlapsAnyRegion(int start, int end, IEnumerable<(int Start, int End)> regions)
    {
        foreach ((int rStart, int rEnd) in regions)
        {
            if (start < rEnd && end > rStart)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Plain-needle-only matcher: walks the haystack and accepts only Plain
    /// candidates. Used by the attribution pass to prevent a bare-number fact
    /// like "100" from claiming a typed haystack span like "100%" (Concentration
    /// percent) — semantic mismatch even though values agree.
    /// </summary>
    private static QuantityMatch? FindPlainOnlyMatch(
        Quantity needle,
        IReadOnlyList<Quantity> haystack,
        HashSet<(int Start, int End)> claimedSpans)
    {
        QuantityMatch? best = null;
        double bestDelta = double.MaxValue;

        foreach (Quantity candidate in haystack)
        {
            if (candidate.Kind is not UnitKind.Plain)
            {
                continue;
            }

            if (claimedSpans.Contains((candidate.Start, candidate.End)))
            {
                continue;
            }

            if (!QuantityMatcher.Equivalent(needle, candidate))
            {
                continue;
            }

            double delta = Math.Abs(needle.BaseValue - candidate.BaseValue);
            if (best is null || delta < bestDelta)
            {
                best = new QuantityMatch(MatchTier.PlainWildcard, candidate);
                bestDelta = delta;
            }
        }

        return best;
    }

    /// <summary>
    /// Wraps the matched span, and every other span that writes the figure the same way: the
    /// literal tier marks each occurrence of a figure, so a figure found by quantity is marked at
    /// each occurrence too.
    /// </summary>
    private static void AddFallbackSpan(
        QuantityMatch match,
        Fact fact,
        IReadOnlyList<Quantity> haystack,
        Dictionary<string, string> factToCitationId,
        List<MarkerSpan> spans,
        HashSet<(int Start, int End)> claimedSpans)
    {
        if (!factToCitationId.TryGetValue(fact.Value, out string? citId))
        {
            return;
        }

        Quantity matched = match.Quantity;

        if (IsBareSingleDigit(matched.Surface))
        {
            return;
        }

        if (OverlapsAnyRegion(matched.Start, matched.End, claimedSpans))
        {
            return;
        }

        // If the matched surface in the haystack is byte-identical to the
        // fact's value, Tier 1 regex already had its chance. Its word-boundary
        // lookarounds (date-separator / percentage / decimal-continuation
        // guards) deliberately blocked the match, and the Quantity-fallback
        // pass must not re-introduce it. Different surfaces (rephrasings) do
        // proceed — they are the recovery cases this pass exists to handle.
        if (string.Equals(matched.Surface, fact.Value, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string wrapped = $"<fact id=\"{citId}\">{matched.Surface}</fact>";
        foreach (Quantity occurrence in haystack.Where(candidate =>
            string.Equals(candidate.Surface, matched.Surface, StringComparison.Ordinal)
            && !OverlapsAnyRegion(candidate.Start, candidate.End, claimedSpans)))
        {
            spans.Add(new MarkerSpan(occurrence.Start, occurrence.End, wrapped, citId, TracedMarker.Quantity));
            _ = claimedSpans.Add((occurrence.Start, occurrence.End));
        }
    }

    private static void AddRestatedDateSpans(
        string rawResponse,
        IReadOnlyList<Fact> facts,
        IReadOnlySet<string> consumedValues,
        Dictionary<string, string> factToCitationId,
        IReadOnlyList<(int Start, int End)> unwrappableRegions,
        List<MarkerSpan> spans,
        HashSet<(int Start, int End)> claimedSpans)
    {
        List<Fact> unmatchedDates = [.. facts
            .Where(f => IsCitableNumericalFact(f)
                && f.Quantity is null
                && !consumedValues.Contains(f.Value)
                && DateEquivalence.IsDate(f.Value))
            .GroupBy(f => f.Value)
            .Select(g => g.First())];

        if (unmatchedDates.Count == 0)
        {
            return;
        }

        List<(int Position, string Value)> rawDates = NumericalDataExtractor.ExtractDatePositions(rawResponse)
            .Where(d => !OverlapsAnyRegion(d.Position, d.Position + d.Value.Length, unwrappableRegions))
            .ToList();

        foreach (Fact fact in unmatchedDates)
        {
            if (!factToCitationId.TryGetValue(fact.Value, out string? citId))
            {
                continue;
            }

            // A surface identical to the fact value is one Tier 1's boundary
            // guards refused on purpose; this pass exists for restatements only.
            foreach ((int position, string surface) in rawDates)
            {
                int end = position + surface.Length;
                if (OverlapsAnyRegion(position, end, claimedSpans)
                    || string.Equals(surface, fact.Value, StringComparison.OrdinalIgnoreCase)
                    || !DateEquivalence.ValueStatesDate(surface, fact.Value))
                {
                    continue;
                }

                spans.Add(new MarkerSpan(position, end, $"<fact id=\"{citId}\">{surface}</fact>", citId, TracedMarker.Date));
                _ = claimedSpans.Add((position, end));
            }
        }
    }

    public static string? ClearFactTags(string? rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return rawResponse;
        }

        return FactTags.Strip(rawResponse)?.Trim();
    }

    public void ProcessConfidenceScore(string rawResponse, BlockAnnotations blockAnnotations)
    {
        if (string.IsNullOrWhiteSpace(rawResponse) || blockAnnotations is null)
        {
            return;
        }

        double? confidenceScore = _confidenceScoreExtractor.ExtractConfidenceScore(rawResponse);

        if (confidenceScore is not null)
        {
            blockAnnotations.AddConfidenceScore(confidenceScore.Value);
        }
    }

    public void ProcessSources(string rawResponse, BlockAnnotations blockAnnotations)
    {
        if (string.IsNullOrWhiteSpace(rawResponse) || blockAnnotations is null)
        {
            return;
        }

        List<string> source = _sourceExtractor.ExtractSources(rawResponse);

        blockAnnotations.AddSources(source);
    }

    public static string ClearConfidenceTags(string textToClear)
    {
        return ConfidenceLevelRegex().Replace(textToClear, string.Empty).Trim();
    }

    public static string ClearSourceTags(string textToClear)
    {
        return SourceRegex().Replace(textToClear, string.Empty).Trim();
    }

    /// <summary>
    /// The propagation step a block runs over the facts of the blocks it references: each
    /// referenced fact's sources flow onto this block's facts that cite it, then the
    /// referenced facts whose value this block's output restates are inherited by reference.
    /// Expansion runs first on purpose — an inherited fact is shared and must not change
    /// after it is added.
    /// </summary>
    public static void InheritReferencedFacts(
        BlockAnnotations blockAnnotations,
        IEnumerable<Fact> referencedFacts,
        string? rawResponse)
    {
        List<Fact> facts = OneMeaningPerValue(blockAnnotations, referencedFacts, rawResponse);

        foreach (Fact fact in facts)
        {
            ExpandSourcesFromReferencedFact(blockAnnotations, fact);
        }

        blockAnnotations.AddReferencedFacts(facts, rawResponse);
    }

    /// <summary>
    /// One referenced fact per value. Facts that share a value but cite different sources can
    /// mean different things — £1.2m of overdraft on one page, £1.2m of revenue on another, 4.7% of
    /// the shares in one block's sentence and a 4.7% fall in commissioning in another's. The one
    /// whose sources' words around the value share the most with this block's words around it is
    /// kept. When two or more pages or links compete and tie, none of them is kept and the value is
    /// recorded as undecided, so the figure stays unmarked rather than linked to the wrong page —
    /// unless a fact that cites only blocks is there to fall back on, or the tied facts share a page
    /// or link, which then states the value for all of them. When at most one page competes, the
    /// facts that cite blocks compete too, and a tie keeps the first of the tied. A block that restates the
    /// value only in another form gives no words to compare, so the first fact is kept.
    /// </summary>
    private static List<Fact> OneMeaningPerValue(
        BlockAnnotations blockAnnotations,
        IEnumerable<Fact> referencedFacts,
        string? rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return [.. referencedFacts.DistinctBy(fact => fact.Value)];
        }

        var chosen = new List<Fact>();

        foreach (IGrouping<string, Fact> sameValue in referencedFacts.GroupBy(fact => fact.Value))
        {
            if (!sameValue.Skip(1).Any())
            {
                chosen.Add(sameValue.First());
                continue;
            }

            ILookup<bool, Fact> citesAPage = sameValue.ToLookup(fact => PagesAndLinksOf(fact).Any());
            List<Fact> pageFacts = [.. citesAPage[true]
                .DistinctBy(fact => string.Join('|', PagesAndLinksOf(fact).Select(source => source.SourceKey).Order(StringComparer.Ordinal)))];
            List<Fact> blockFacts = [.. citesAPage[false]
                .DistinctBy(fact => string.Join('|', fact.Sources.Select(source => $"{source.SourceKey}:{source.CitedText}").Order(StringComparer.Ordinal)))];

            if (pageFacts.Count + blockFacts.Count < 2)
            {
                chosen.Add(sameValue.First());
                continue;
            }

            HashSet<string> claimWords = ClaimContext.WordsAroundValue(rawResponse, sameValue.Key);
            if (claimWords.Count == 0)
            {
                chosen.Add(sameValue.First());
                continue;
            }

            bool pagesCompete = pageFacts.Count >= 2;
            List<Fact> candidates = pagesCompete ? pageFacts : [.. pageFacts, .. blockFacts];
            List<Fact> best = ClaimContext.TopScoring(candidates, fact => (pagesCompete ? PagesAndLinksOf(fact) : fact.Sources)
                .Select(source => ClaimContext.SharedWords(claimWords, source.CitedText, fact.Value))
                .DefaultIfEmpty(0)
                .Max());

            if (best.Count == 1 || (pagesCompete && ShareAPageOrLink(best)))
            {
                chosen.Add(best[0]);
                blockAnnotations.Trace.RejectUpstream(
                    CitationTraceReason.OtherMeaningChosen,
                    sameValue.Key,
                    candidates.Except<Fact>(best, ReferenceEqualityComparer.Instance));
            }
            else if (!pagesCompete)
            {
                chosen.Add(best[0]);
            }
            else if (blockFacts.FirstOrDefault() is Fact blockOnly)
            {
                chosen.Add(blockOnly);
                blockAnnotations.Trace.RejectUpstream(CitationTraceReason.TiedPagesKeptBlockFact, sameValue.Key, pageFacts);
            }
            else
            {
                blockAnnotations.UndecidedFacts.Add(sameValue.First());
                blockAnnotations.Trace.RejectUpstream(CitationTraceReason.SameValueDifferentMeanings, sameValue.Key, pageFacts);
            }
        }

        return chosen;
    }

    private static bool ShareAPageOrLink(List<Fact> facts)
    {
        HashSet<string> shared = [.. PagesAndLinksOf(facts[0]).Select(source => source.SourceKey)];
        foreach (Fact fact in facts.Skip(1))
        {
            shared.IntersectWith(PagesAndLinksOf(fact).Select(source => source.SourceKey));
        }

        return shared.Count > 0;
    }

    private static IEnumerable<BaseSource> PagesAndLinksOf(Fact fact)
    {
        return fact.Sources.Where(source => source is not BlockSource);
    }

    /// <summary>
    /// For each fact in the annotations, if a BlockSource's CitedText contains the
    /// referenced fact's value (literally or via kind-aware quantity equivalence)
    /// AND the local fact's value is related to it (literally or via kind-aware
    /// quantity equivalence), copies non-Block sources from the referenced fact
    /// into the fact's sources.
    /// </summary>
    public static void ExpandSourcesFromReferencedFact(
        BlockAnnotations blockAnnotations,
        Fact referencedFact)
    {
        foreach (Fact fact in blockAnnotations.Facts)
        {
            List<BaseSource> sourcesToAdd = [];

            foreach (BaseSource source in fact.Sources)
            {
                if (source is not BlockSource blockSource)
                {
                    continue;
                }

                IReadOnlyList<Quantity> citedTextQuantities = UnitExtractor.Extract(blockSource.CitedText);

                if (!CitedTextMentionsFact(blockSource.CitedText, citedTextQuantities, referencedFact))
                {
                    continue;
                }

                if (!AreRelatedFacts(
                    fact.Value, fact.Quantity,
                    referencedFact.Value, referencedFact.Quantity))
                {
                    continue;
                }

                foreach (BaseSource refSource in referencedFact.Sources)
                {
                    if (fact.Sources.Contains(refSource)
                        || refSource is BlockSource)
                    {
                        continue;
                    }

                    sourcesToAdd.Add(refSource);
                }
            }

            if (sourcesToAdd.Count > 0)
            {
                blockAnnotations.Trace.Expanded(fact, referencedFact);
            }

            fact.AddSources(sourcesToAdd);
        }
    }

    /// <summary>
    /// Flattens citation sources:
    /// Rule 2: If any doc/url sources exist directly → keep only those, drop block sources.
    /// Rule 3: If only block sources → pull up ALL doc/url from upstream tree, keep only those.
    /// For a numerical citation a pulled-up page or link must state <paramref name="factValue"/>:
    /// an upstream block can cite a page for a whole sentence whose figure that page never prints.
    /// A page that states only a rounding of it must also share a word with
    /// <paramref name="claimWords"/>, the paragraph's words around the figure, or a figure about one
    /// thing reaches a page about another.
    /// Rule 4: If no doc/url found anywhere → keep block sources as fallback;
    /// <see cref="MarkMatchingSources"/> then points each at the pages and links behind its block.
    /// </summary>
    public static TracedFlattening FlattenCitationSources(Citation citation, string factValue, HashSet<string> claimWords)
    {
        List<BaseSource> directDocUrl = [.. citation.Sources.Where(s => s is DocumentSource or UrlSource)];

        if (directDocUrl.Count > 0)
        {
            // Rule 2: doc/url sources exist — keep only those
            citation.Sources = directDocUrl;
            return TracedFlattening.Direct;
        }

        // Rule 3: only block sources — pull up ALL doc/url from upstream tree
        var pulledUp = new List<BaseSource>();
        var seen = new HashSet<string>();

        foreach (BlockSource blockSource in citation.Sources.OfType<BlockSource>())
        {
            CollectDocUrlSourcesRecursively(blockSource, pulledUp, seen);
        }

        if (citation.IsNumerical)
        {
            var figure = new Fact(factValue) { IsNumerical = true };
            pulledUp = [.. pulledUp.Where(source => ExcerptStatesValue(source, figure, claimWords))];
        }

        if (pulledUp.Count > 0)
        {
            citation.Sources = pulledUp;
            return TracedFlattening.FromUpstream;
        }

        // Rule 4: no doc/url found anywhere — keep block sources as-is
        return TracedFlattening.BlockFallback;
    }

    /// <summary>
    /// Whether a source's excerpt states the figure: as written or in another form, or for a page
    /// as a statement cell read at the scale its own header prints ("2,506" under "£'000"). A
    /// rounding alone counts only when the excerpt's words around it share one with the claim's.
    /// </summary>
    private static bool ExcerptStatesValue(BaseSource source, Fact figure, HashSet<string> claimWords)
    {
        string excerpt = source.CitedText;
        bool states = NumericalGrounding.TextContainsValue(excerpt, figure.Value)
            || (source is DocumentSource
                && StatementScale.Of(excerpt) is StatementScale scale
                && figure.Quantity is Quantity claimed
                && scale.StatesScaled(UnitExtractor.Extract(excerpt), claimed));

        if (!states || claimWords.Count == 0)
        {
            return states;
        }

        (int index, int length, bool isRounded) = new FigureFinder(excerpt).Locate(figure);
        return index < 0 || !isRounded || ClaimContext.SharedWords(claimWords, excerpt, index, length) > 0;
    }

    /// <summary>
    /// The rule 4 step: each block source a citation still carries gets, as
    /// <see cref="BlockSource.MatchingSources"/>, every source from its block's cited list whose
    /// excerpt states <paramref name="factValue"/> under the grounding rules, one per page or link,
    /// excerpted around the fact. Every excerpt of a link is tested, not only the first, because a
    /// research provider returns several per page. The citation's block sources are its own
    /// copies (<see cref="ExpandUpstreamSources(BlockSource, WorkflowAnnotationsContext, string?)"/>),
    /// so they are marked in place.
    /// </summary>
    public static void MarkMatchingSources(
        Citation citation,
        string factValue,
        BlockCitedSourcesCollector collector)
    {
        foreach (BlockSource blockSource in citation.Sources.OfType<BlockSource>())
        {
            blockSource.MatchingSources = [.. DeduplicateSources([.. collector.CitedSourcesOf(blockSource.BlockId)
                    .Where(source => NumericalGrounding.TextContainsValue(source.CitedText, factValue))])
                .Select(source => BoundedSource(source, factValue))];
        }
    }

    /// <summary>
    /// Recursively collects all DocumentSource and UrlSource objects from a
    /// BlockSource's UpstreamSources tree.
    /// </summary>
    private static void CollectDocUrlSourcesRecursively(
        BlockSource blockSource,
        List<BaseSource> collected,
        HashSet<string> seen)
    {
        foreach (BaseSource upstream in blockSource.UpstreamSources)
        {
            if (upstream is BlockSource nestedBlock)
            {
                CollectDocUrlSourcesRecursively(nestedBlock, collected, seen);
            }
            else if (seen.Add(upstream.SourceKey))
            {
                collected.Add(upstream);
            }
        }
    }

    /// <summary>
    /// The block source as the citation of <paramref name="factValue"/> reads it: a copy whose
    /// UpstreamSources are the sources of the referenced block's facts that its CitedText states,
    /// each nested block source expanded the same way. Two-tier matching:
    /// - Tier 1 (precise): BlockSource.CitedText contains Fact.Value
    /// - Tier 2 (fallback): Fact.Value contains BlockSource.CitedText (only if no direct matches)
    /// When <paramref name="factValue"/> is provided, an upstream fact must also be the same fact
    /// as the citation's, so a CitedText that is a full paragraph does not pull in every number
    /// on it. Deduplicates sources by their unique identifier (PageId for documents, Url for URLs,
    /// BlockId for blocks).
    /// <para>
    /// The run's annotation graph is only read. The block source a fact holds is the same object
    /// for every number its excerpt states, so sources written into it for one number would reach
    /// the citation of another.
    /// </para>
    /// </summary>
    /// <param name="blockSource">The BlockSource to expand</param>
    /// <param name="context">The workflow annotations context with block annotations</param>
    /// <param name="factValue">Optional fact value to scope matching — only upstream facts
    /// that are the same fact as this value are included.</param>
    public static BlockSource ExpandUpstreamSources(
        BlockSource blockSource,
        WorkflowAnnotationsContext context,
        string? factValue = null)
    {
        Quantity? scopeQuantity = factValue is null
            ? null
            : UnitExtractor.TryExtractSingle(factValue);

        return ExpandUpstreamSources(blockSource, context, [], factValue, scopeQuantity);
    }

    private static BlockSource ExpandUpstreamSources(
        BlockSource blockSource,
        WorkflowAnnotationsContext context,
        HashSet<string> visited,
        string? scopeValue,
        Quantity? scopeQuantity)
    {
        var expanded = (BlockSource)blockSource.ShallowCopy();
        expanded.UpstreamSources = [];

        // A block already reached is not expanded again, which ends a circular chain.
        if (!visited.Add(blockSource.BlockId)
            || context.GetAnnotationsForBlock(blockSource.BlockId) is not BlockAnnotations referencedAnnotations)
        {
            return expanded;
        }

        // Extract quantities from the BlockSource.CitedText once — Tier 2/3
        // matching for every upstream fact reuses this list.
        IReadOnlyList<Quantity> citedTextQuantities = UnitExtractor.Extract(blockSource.CitedText);
        Quantity? citedTextSingleQuantity = UnitExtractor.TryExtractSingle(blockSource.CitedText);

        List<Fact> directCandidates = [];
        List<Fact> fallbackCandidates = [];

        foreach (Fact fact in referencedAnnotations.Facts)
        {
            // TIER 1: Precise match — cited text contains fact value (literal,
            // or via kind-aware quantity equivalence — handles re-synthesis like
            // "$184 billion" against CitedText with "$183,866 million").
            if (!CitedTextMentionsFact(blockSource.CitedText, citedTextQuantities, fact))
            {
                continue;
            }

            // Scoping: when scope is provided, only include upstream facts that
            // are the same fact as the downstream citation. Pre-quantity scoping
            // accepted symmetric substring containment, which let "712" pull
            // sources from upstream "712 million" (different quantities, same
            // bare digits — false positive). Quantity-aware scoping rejects
            // those cross-magnitude collisions.
            if (scopeValue is not null
                && !AreRelatedFacts(scopeValue, scopeQuantity, fact.Value, fact.Quantity))
            {
                continue;
            }

            directCandidates.Add(fact);
        }

        // TIER 2: Fallback — only if no direct matches found. Used when the
        // BlockSource's CitedText is itself a small numeric snippet that the
        // upstream fact's value contains.
        if (directCandidates.Count == 0)
        {
            foreach (Fact fact in referencedAnnotations.Facts)
            {
                if (!FactValueMentionsCitedText(fact, blockSource.CitedText, citedTextSingleQuantity))
                {
                    continue;
                }

                if (scopeValue is not null
                    && !AreRelatedFacts(scopeValue, scopeQuantity, fact.Value, fact.Quantity))
                {
                    continue;
                }

                fallbackCandidates.Add(fact);
            }
        }

        List<Fact> selected = directCandidates.Count > 0 ? directCandidates : fallbackCandidates;

        // Best-match-wins among quantitied candidates: when scope has a
        // structured quantity, drop quantitied upstream facts that aren't
        // closest to scope in BaseValue. Closes the Path 3 residual where two
        // upstream facts within the same kind's tolerance (e.g. "85%" and
        // "85.05%" both Concentration) would both contribute to a downstream
        // "85%" citation. No-Quantity candidates pass through unchanged —
        // they were admitted by substring relation, not numeric equivalence,
        // so they're not comparable on the BaseValue axis.
        if (scopeQuantity is not null)
        {
            selected = SelectClosestByQuantity(selected, scopeQuantity);
        }

        var sourcesToAdd = new List<BaseSource>();
        foreach (Fact fact in selected)
        {
            sourcesToAdd.AddRange(fact.Sources);
        }

        expanded.UpstreamSources = [.. DeduplicateSources(sourcesToAdd)
            .Select(source => source is BlockSource nestedBlock
                ? ExpandUpstreamSources(nestedBlock, context, visited, scopeValue, scopeQuantity)
                : source)];

        return expanded;
    }

    private static bool CitedTextMentionsFact(
        string citedText,
        IReadOnlyList<Quantity> citedTextQuantities,
        Fact fact)
    {
        if (citedText.Contains(fact.Value, StringComparison.OrdinalIgnoreCase)
            || DateEquivalence.TextStatesDate(citedText, fact.Value))
        {
            return true;
        }

        if (fact.Quantity is null)
        {
            return false;
        }

        return QuantityMatcher.FindBestMatch(fact.Quantity, citedTextQuantities) is not null;
    }

    private static bool FactValueMentionsCitedText(
        Fact fact,
        string citedText,
        Quantity? citedTextSingleQuantity)
    {
        if (fact.Value.Contains(citedText, StringComparison.OrdinalIgnoreCase)
            || DateEquivalence.TextStatesDate(fact.Value, citedText))
        {
            return true;
        }

        if (fact.Quantity is null || citedTextSingleQuantity is null)
        {
            return false;
        }

        return QuantityMatcher.Equivalent(fact.Quantity, citedTextSingleQuantity);
    }

    /// <summary>
    /// Whether two facts represent the same claim. Equality on values short-
    /// circuits to true. When both sides are quantitied, relatedness goes
    /// through kind-aware <see cref="QuantityMatcher.Equivalent"/> only —
    /// substring containment between numerical values is dropped because it
    /// produces false positives: atomic <c>"712"</c> structurally contained
    /// in compound <c>"712 million"</c> at completely different magnitudes,
    /// or <c>"6.49%"</c> structurally containing <c>"49%"</c> at completely
    /// different ratios. Substring fallback only fires when <em>both</em>
    /// sides are non-quantitied (broad-broad relation, where structural
    /// containment is the strongest available signal); two non-quantitied
    /// values that read as the same calendar date in different forms are
    /// related before that fallback, since a date's forms share no
    /// substring. When exactly one
    /// side is quantitied — typically a numerical scope vs a broad upstream
    /// sentence that happens to mention the number — substring is refused
    /// too: a broad sentence containing a number isn't "about" that number,
    /// and pulling every co-occurring source on the sentence is the
    /// production-trace 16-unrelated-sources shape.
    /// </summary>
    internal static bool AreRelatedFacts(string value1, Quantity? q1, string value2, Quantity? q2)
    {
        if (string.Equals(value1, value2, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (q1 is not null && q2 is not null)
        {
            return QuantityMatcher.Equivalent(q1, q2);
        }

        // Asymmetric case: one quantitied, one not. Refuse substring
        // fallback — a broad sentence containing the scope value's surface
        // form pulls every co-occurring source on that sentence (the
        // $747.0M-with-16-unrelated-sources production trace).
        if (q1 is not null || q2 is not null)
        {
            return false;
        }

        return DateEquivalence.AreSameDate(value1, value2)
            || value1.Contains(value2, StringComparison.OrdinalIgnoreCase)
            || value2.Contains(value1, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Among quantitied candidates, keep only those at the minimum
    /// <see cref="QuantityMatcher.RankingDelta"/> to <paramref name="scope"/> —
    /// the canonical scale for typed pairs, so an exactly-equal converted
    /// candidate is not scored as the farthest one. Candidates without a
    /// Quantity pass through unchanged — they entered via substring relation,
    /// not numeric equivalence, so the distance axis is undefined for them.
    /// Tie at min-delta keeps every tied candidate (genuine ambiguity, not
    /// for the matcher to resolve).
    /// </summary>
    private static List<Fact> SelectClosestByQuantity(List<Fact> candidates, Quantity scope)
    {
        if (candidates.Count <= 1)
        {
            return candidates;
        }

        double minDelta = double.MaxValue;
        foreach (Fact f in candidates)
        {
            if (f.Quantity is null)
            {
                continue;
            }

            double delta = QuantityMatcher.RankingDelta(scope, f.Quantity);
            if (delta < minDelta)
            {
                minDelta = delta;
            }
        }

        if (minDelta == double.MaxValue)
        {
            return candidates;
        }

        var result = new List<Fact>();
        foreach (Fact f in candidates)
        {
            if (f.Quantity is null)
            {
                result.Add(f);
                continue;
            }

            if (QuantityMatcher.RankingDelta(scope, f.Quantity) <= minDelta)
            {
                result.Add(f);
            }
        }

        return result;
    }

    /// <summary>
    /// Deduplicates sources by their unique identifier.
    /// - DocumentSource: PageId
    /// - UrlSource: Url
    /// - BlockSource: BlockId
    /// </summary>
    internal static List<BaseSource> DeduplicateSources(List<BaseSource> sources)
    {
        var seen = new HashSet<string>();
        var result = new List<BaseSource>();

        foreach (BaseSource source in sources)
        {
            if (!seen.Add(source.SourceKey))
            {
                continue;
            }

            result.Add(source);
        }

        return result;
    }


    // Tag matcher that treats quoted attribute values as atomic, so a '>'
    // inside an attribute (e.g. <div title="x > 5">) does not end the tag
    // early and leave the rest of the tag wrappable. Written in unrolled-loop
    // form ([^>"']* then repeated quoted-string + [^>"']*) so it stays
    // linear-time: the naive (?:[^>"']+|"...")* form backtracks
    // catastrophically on an unterminated tag-like run.
    [GeneratedRegex("</?[a-zA-Z!][^>\"']*(?:(?:\"[^\"]*\"|'[^']*')[^>\"']*)*>", RegexOptions.Compiled)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"(?<=^[ \t]*)\d{1,3}(?=[.)]\s)", RegexOptions.Multiline)]
    private static partial Regex ListItemNumberRegex();

    [GeneratedRegex(@"<confidence>\d+\.\d+</confidence>", RegexOptions.Singleline)]
    private static partial Regex ConfidenceLevelRegex();

    [GeneratedRegex(@"<source>.*?</source>", RegexOptions.Singleline)]
    private static partial Regex SourceRegex();
}

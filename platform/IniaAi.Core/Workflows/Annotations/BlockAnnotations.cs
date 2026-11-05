using IniaAi.Core.Workflows.Annotations.Quantities;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.Citations.Traces;

namespace IniaAi.Core.Workflows.Annotations;

public class BlockAnnotations
{
    private readonly HashSet<Fact> _referencedFacts = new(ReferenceEqualityComparer.Instance);

    public List<Fact> Facts { get; } = [];

    /// <summary>
    /// Facts from earlier blocks that state one value with different meanings on different
    /// pages, where this block's output did not say which one it means. None of them is
    /// inherited, and the value gets no other citation in this block either.
    /// </summary>
    public List<Fact> UndecidedFacts { get; } = [];
    public List<double> ConfidenceScores { get; } = [];
    public List<string> Sources { get; } = [];

    public CitationTraceRecorder Trace { get; private set; } = new();

    /// <summary>
    /// Each annotation pass records its own trace, so annotations processed again do not carry
    /// the decisions of the pass before.
    /// </summary>
    internal void RestartTrace()
    {
        Trace = new CitationTraceRecorder();
    }

    public void AddFact(Fact fact)
    {
        if (fact is null)
        {
            return;
        }

        if (Facts.Any(existing => existing.Equals(fact)))
        {
            return;
        }

        Facts.Add(fact);
    }

    /// <summary>
    /// Adds referenced facts from upstream blocks when each fact's value is
    /// either literally present in the block's raw response (Tier 1) or has a
    /// quantity-equivalent value mentioned in it (Tier 2 typed equivalence,
    /// Tier 3 Plain wildcard). Pre-quantity propagation was pure substring,
    /// which dropped facts whose surface form differed from the downstream
    /// rendering even when the underlying number was the same.
    /// <para>
    /// Bare Plain numerical facts (Plain Kind, multiplier 1 — a raw integer
    /// or decimal with no unit and no magnitude word) are skipped entirely:
    /// findings.md Path 4 — "712" matching inside "Page 712 of the report"
    /// is the canonical false positive. Plain values with a magnitude
    /// ("712 million") propagate normally. A bare year propagates only when
    /// the words around it here share one with the words around it in a
    /// source: "2025" cited from an advertising forecast says nothing about
    /// "the Employment Rights Act 2025". Non-numerical facts have no Quantity
    /// and rely on substring as the only signal.
    /// </para>
    /// Facts are added by reference — safe because
    /// <c>ExpandSourcesFromReferencedFact</c> (which mutates this.Facts) always
    /// runs before this method, and <c>SplitNumericalFacts</c> passes an inherited
    /// fact through as it is (<see cref="IsReferenced"/>), so shared facts are never
    /// modified after addition.
    /// </summary>
    public void AddReferencedFacts(IEnumerable<Fact> facts, string? rawResponse)
    {
        if (facts is null || string.IsNullOrWhiteSpace(rawResponse))
        {
            return;
        }

        IReadOnlyList<Quantity>? rawResponseQuantities = null;
        List<string>? rawResponseDates = null;
        var heldFacts = new HashSet<Fact>(Facts);
        var claimWordsByYear = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (Fact fact in facts)
        {
            if (fact is null || string.IsNullOrWhiteSpace(fact.Value))
            {
                continue;
            }

            if (fact.Quantity is null)
            {
                if (rawResponse.Contains(fact.Value, StringComparison.OrdinalIgnoreCase)
                    || DateEquivalence.StatedBy(rawResponseDates ??= NumericalDataExtractor.ExtractDates(rawResponse), fact.Value))
                {
                    AddReferencedFact(fact, heldFacts);
                }

                continue;
            }

            if (fact.Quantity.IsBareNumber)
            {
                RejectWhenStated(CitationTraceReason.BareNumberNotInherited, fact, rawResponse);
                continue;
            }

            if (fact.Quantity.Kind is UnitKind.Year && !SourcesSpeakOfTheSameYear(fact, rawResponse, claimWordsByYear))
            {
                RejectWhenStated(CitationTraceReason.YearContextDiffers, fact, rawResponse);
                continue;
            }

            if ((!fact.IsTableCellAtPageScale && NumericalGrounding.StatesWithItsSign(rawResponse, fact.Value))
                || QuantityMatcher.FindBestMatch(fact.Quantity, rawResponseQuantities ??= UnitExtractor.Extract(rawResponse)) is not null)
            {
                AddReferencedFact(fact, heldFacts);
            }
        }
    }

    /// <summary>Whether <paramref name="fact"/> is an earlier block's own object, inherited by reference.</summary>
    public bool IsReferenced(Fact fact)
    {
        return _referencedFacts.Contains(fact);
    }

    private void AddReferencedFact(Fact fact, HashSet<Fact> heldFacts)
    {
        if (!heldFacts.Add(fact))
        {
            return;
        }

        Facts.Add(fact);
        _ = _referencedFacts.Add(fact);
    }

    /// <summary>
    /// Records an earlier block's fact a gate turned down, only when this block's output states its
    /// value: a fact the output never mentions was not going to be inherited anyway.
    /// </summary>
    private void RejectWhenStated(CitationTraceReason reason, Fact fact, string rawResponse)
    {
        if (rawResponse.Contains(fact.Value, StringComparison.OrdinalIgnoreCase)
            && NumericalGrounding.StatesOnNumberBoundaries(rawResponse, fact.Value))
        {
            Trace.RejectUpstream(reason, fact.Value, [fact]);
        }
    }

    private static bool SourcesSpeakOfTheSameYear(Fact year, string rawResponse, Dictionary<string, HashSet<string>> claimWordsByYear)
    {
        if (!claimWordsByYear.TryGetValue(year.Value, out HashSet<string>? claimWords))
        {
            claimWords = ClaimContext.WordsNearValue(rawResponse, year.Value);
            claimWordsByYear[year.Value] = claimWords;
        }

        return year.Sources.Any(source => ClaimContext.SpeaksOfTheSameThing(claimWords, source.CitedText, year.Value));
    }

    public void AddFacts(IEnumerable<Fact> facts)
    {
        if (facts is null)
        {
            return;
        }

        foreach (Fact fact in facts)
        {
            AddFact(fact);
        }
    }

    public void AddConfidenceScore(double score)
    {
        ConfidenceScores.Add(score);
    }

    public void AddConfidenceScores(IEnumerable<double> scores)
    {
        if (scores is null)
        {
            return;
        }

        ConfidenceScores.AddRange(scores);
    }

    public void AddSources(List<string> source)
    {
        if (source is null)
        {
            return;
        }

        foreach (string src in source)
        {
            if (string.IsNullOrWhiteSpace(src)
                || Sources.Contains(src.Trim()))
            {
                continue;
            }

            Sources.Add(src.Trim());
        }
    }

    public List<string> GetReferencedBlockIds()
    {
        var seen = new HashSet<string>();
        var referencedBlockIds = new List<string>();

        foreach (Fact fact in Facts)
        {
            foreach (BaseSource source in fact.Sources)
            {
                if (source is BlockSource blockSource
                    && seen.Add(blockSource.BlockId))
                {
                    referencedBlockIds.Add(blockSource.BlockId);
                }
            }
        }

        return referencedBlockIds;
    }
}

using IniaAi.Domain.Entities.Citations.Sources;

namespace IniaAi.Core.Workflows.Annotations;

/// <summary>
/// Shared <see cref="Fact"/>-building pipeline for Perplexity and Parallel.ai
/// response parsers. Both parsers discover (claim, sources) pairs from their
/// respective JSON shapes — Perplexity by splitting on <c>[N]</c> marker runs,
/// Parallel.ai by resolving <c>basis[].field</c> paths into structured content
/// — and then run an identical broad-fact + grounded-numerical-sub-fact
/// pipeline. This builder is that shared back half.
///
/// For each pair: emit one broad <see cref="Fact"/> carrying every
/// <paramref name="sources"/> entry, then emit one numerical sub-fact per
/// value returned by <see cref="NumericalDataExtractor.Extract"/> whose value
/// is grounded by at least one of the parent sources' <c>CitedText</c>.
/// Sub-facts whose value isn't grounded by any parent snippet are dropped —
/// the broad fact still preserves the data via its own source list.
/// </summary>
public static class CitationFactBuilder
{
    /// <summary>
    /// Builds the broad-fact + grounded-sub-fact list for the given claim
    /// stream. Empty / whitespace-only claims are skipped silently.
    /// </summary>
    public static List<Fact> BuildFacts<TSource>(
        IEnumerable<(string Claim, IReadOnlyList<TSource> Sources)> claims)
        where TSource : BaseSource
    {
        var facts = new List<Fact>();

        foreach ((string claim, IReadOnlyList<TSource> sources) in claims)
        {
            string trimmed = claim?.Trim() ?? string.Empty;
            if (trimmed.Length == 0)
            {
                continue;
            }

            var broadFact = new Fact(trimmed) { IsNumerical = false };
            foreach (TSource source in sources)
            {
                broadFact.AddSource(source);
            }

            facts.Add(broadFact);

            foreach (string numericalValue in NumericalDataExtractor.Extract(trimmed))
            {
                List<TSource> grounded = GroundSourcesForValue(sources, numericalValue);
                if (grounded.Count == 0)
                {
                    continue;
                }

                var numericalFact = new Fact(numericalValue) { IsNumerical = true };
                foreach (TSource source in grounded)
                {
                    numericalFact.AddSource(source);
                }

                facts.Add(numericalFact);
            }
        }

        return facts;
    }

    /// <summary>
    /// Returns the parent sources whose <c>CitedText</c> grounds
    /// <paramref name="value"/>, using a three-step strategy:
    /// (1) literal substring, (2) multiplier-stripped substring,
    /// (3) kind-aware quantity equivalence or the same calendar date in
    /// another form via <see cref="NumericalGrounding.StatesEquivalentValue"/>.
    /// Per-step fallback —
    /// later steps only run if earlier ones found nothing. Returned
    /// sources are fresh copies so the broad fact and the numerical
    /// sub-fact each own their instances.
    /// </summary>
    private static List<TSource> GroundSourcesForValue<TSource>(
        IReadOnlyList<TSource> parentSources,
        string value)
        where TSource : BaseSource
    {
        List<TSource> literal = Grounded(parentSources,
            source => source.CitedText.Contains(value, StringComparison.OrdinalIgnoreCase));
        if (literal.Count > 0)
        {
            return literal;
        }

        string? coreNumber = FactExtractor.StripMultiplierSuffix(value);
        if (coreNumber is not null)
        {
            List<TSource> stripped = Grounded(parentSources,
                source => source.CitedText.Contains(coreNumber, StringComparison.OrdinalIgnoreCase));
            if (stripped.Count > 0)
            {
                return stripped;
            }
        }

        return Grounded(parentSources,
            source => NumericalGrounding.StatesEquivalentValue(source.CitedText, value));
    }

    private static List<TSource> Grounded<TSource>(
        IReadOnlyList<TSource> sources,
        Func<TSource, bool> predicate)
        where TSource : BaseSource
    {
        return [.. sources.Where(predicate).Select(source => (TSource)source.ShallowCopy())];
    }
}

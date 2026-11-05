using System.Text.RegularExpressions;
using IniaAi.Core.Common.Helpers;
using IniaAi.Core.Workflows.Annotations.Quantities;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.Citations.Traces;
using IniaAi.Domain.Entities.Workflows.Blocks;

namespace IniaAi.Core.Workflows.Annotations;

/// <summary>
/// A figure a block states with no fact behind it is cited to the input block whose output states
/// it: the block that produced it. A converted amount, a ratio or a growth rate is printed on no
/// page, so grounding drops it, and a later block can inherit only facts that exist. Without this
/// step such a figure reaches the report unmarked; with it, the report cites the producing block
/// and lists that block's pages as its Drawn-from list. A page is never cited for a figure it does
/// not print.
/// </summary>
public static partial class ProducingBlockCitations
{
    public static void CiteProducingBlocks(
        BlockAnnotations annotations,
        string? rawResponse,
        IEnumerable<Block> referencedBlocks)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return;
        }

        List<(Block Block, FigureFinder Output)> inputs = [.. referencedBlocks
            .Where(block => !string.IsNullOrWhiteSpace(block.BlockResponse?.RawResponse))
            .Select(block => (block, new FigureFinder(block.BlockResponse!.RawResponse!)))];

        if (inputs.Count == 0)
        {
            return;
        }

        List<(int Start, int End)> tagRegions = BlockAnnotationProcessor.ComputeHtmlTagRegions(rawResponse);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach ((int position, string figure) in NumericalDataExtractor.ExtractPositions(rawResponse))
        {
            if (!seen.Add(figure)
                || BlockAnnotationProcessor.OverlapsAnyRegion(position, position + figure.Length, tagRegions))
            {
                continue;
            }

            var fact = new Fact(figure) { IsNumerical = true };
            if (!IsDressedFigure(fact) || HasFact(annotations, fact))
            {
                continue;
            }

            if (IsUndecided(annotations, fact))
            {
                annotations.Trace.Reject(CitationTraceReason.UndecidedMeaning, figure);
                continue;
            }

            HashSet<string> claimWords = ClaimContext.WordsAround(rawResponse, position, figure.Length);
            List<(Block Block, FigureFinder Output, int Index, int Length, int Score)> producers = ClaimContext.TopScoring(
                inputs.Select(input => (input.Block, input.Output, Span: input.Output.Locate(fact)))
                    .Where(found => found.Span.Index >= 0)
                    .Select(found => (found.Block, found.Output, found.Span.Index, found.Span.Length, found.Span.IsRounded,
                        Score: ClaimContext.SharedWords(claimWords, found.Output.Text, found.Span.Index, found.Span.Length)))
                    .Where(found => !found.IsRounded || found.Score > 0)
                    .Select(found => (found.Block, found.Output, found.Index, found.Length, found.Score)),
                found => found.Score);

            if (producers.Count == 0)
            {
                annotations.Trace.Reject(CitationTraceReason.NoProducerStatesFigure, figure);
                continue;
            }

            fact.AddSources(producers.Select(found => SourceAt(found.Block, found.Output.Text, found.Index, found.Length)));
            annotations.AddFact(fact);
            annotations.Trace.Producer(fact, producers[0].Score);
        }
    }

    /// <summary>
    /// A figure dressed as one: a currency amount, a percentage, a multiple or a calendar date
    /// to the month. A bare number, a bare year and a period label such as FY2025 say nothing on
    /// their own, so citing one would link whatever else shares its digits.
    /// </summary>
    internal static bool IsDressedFigure(Fact figure)
    {
        if (figure.Quantity is Quantity quantity)
        {
            return !quantity.IsBareNumber && quantity.Kind is not UnitKind.Year;
        }

        return DateEquivalence.IsDate(figure.Value) || MultipleRegex().IsMatch(figure.Value);
    }

    private static bool HasFact(BlockAnnotations annotations, Fact figure)
    {
        return annotations.Facts.Where(fact => fact.IsNumerical).Any(fact => IsSameFigure(fact, figure));
    }

    /// <summary>
    /// Whether two pages state the figure with different meanings and the claim did not say which
    /// one it means: then no producer is cited either, because each would lead back to one of
    /// those pages.
    /// </summary>
    private static bool IsUndecided(BlockAnnotations annotations, Fact figure)
    {
        return annotations.UndecidedFacts.Any(fact => IsSameFigure(fact, figure));
    }

    /// <summary>
    /// The same figure in another form: "$55.1 million" is "$55.1m", and a fact of -42.38% is the
    /// figure "fell 42.38%". A figure written without a minus is compared by magnitude, because
    /// prose often carries the sign in words its value does not hold; a -5% fact therefore also
    /// stands in for "rose 5%", which leaves that figure unlinked rather than linked to the wrong
    /// source. A figure written with a minus states its sign, so "-$3.3mn" is not a $3.3mn fact. A rounded value is
    /// another figure: a fact of 42.4% is not 42.38%, so it neither stands in for it nor keeps it
    /// from its producer.
    /// </summary>
    private static bool IsSameFigure(Fact fact, Fact figure)
    {
        if (string.Equals(fact.Value, figure.Value, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (fact.Quantity is Quantity known && figure.Quantity is Quantity stated)
        {
            return known.Kind == stated.Kind
                && (stated.BaseValue < 0
                    ? QuantityMatcher.Equivalent(known, stated, QuantityMatcher.SAME_AMOUNT_RELATIVE_ERROR)
                    : QuantityMatcher.Equivalent(MagnitudeOf(known), stated, QuantityMatcher.SAME_AMOUNT_RELATIVE_ERROR));
        }

        return fact.Quantity is null
            && figure.Quantity is null
            && DateEquivalence.AreSameDate(fact.Value, figure.Value);
    }

    private static Quantity MagnitudeOf(Quantity quantity)
    {
        return quantity.BaseValue < 0 ? quantity.Magnitude() : quantity;
    }

    private static BlockSource SourceAt(Block block, string output, int index, int length)
    {
        return new BlockSource
        {
            BlockId = block.Id,
            BlockTitle = RequestContentSequenceHelper.CitationTitleOf(block, block.ReplacementTag),
            CitedText = TextPassage.SentenceAround(output, index, index + length, BlockAnnotationProcessor.MAX_CITED_TEXT_LENGTH),
        };
    }

    [GeneratedRegex(@"^-?(?:\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)x$")]
    private static partial Regex MultipleRegex();
}

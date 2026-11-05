using IniaAi.Core.Workflows.Annotations.Quantities;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.Documents;

namespace IniaAi.Core.Workflows.Annotations;

/// <summary>
/// The run's pages by id for one grounding pass, with what each page's text yields — its
/// statement scale and its quantities — read once rather than once per figure.
/// </summary>
internal sealed class PageIndex(IReadOnlyList<Page> pages)
{
    private readonly Dictionary<string, Page> _pagesById = pages
        .DistinctBy(page => page.Id)
        .ToDictionary(page => page.Id);

    // Keyed by the string instance: a page's text and a shared excerpt are the same object on
    // every lookup, and hashing a whole page per figure would cost more than the cache saves.
    private readonly Dictionary<string, StatementScale?> _scaleByText = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, IReadOnlyList<Quantity>> _quantitiesByText = new(ReferenceEqualityComparer.Instance);

    /// <summary>The text of the page with <paramref name="pageId"/>, or null when it is not in the run.</summary>
    public string? PageTextOf(string pageId)
    {
        return _pagesById.GetValueOrDefault(pageId)?.Text is { Length: > 0 } text ? text : null;
    }

    /// <summary>The page's own text, or the source's excerpt when the page is out of scope.</summary>
    public string TextOf(DocumentSource source)
    {
        return PageTextOf(source.PageId) ?? source.CitedText;
    }

    public StatementScale? ScaleOf(DocumentSource source)
    {
        string text = TextOf(source);
        if (!_scaleByText.TryGetValue(text, out StatementScale? scale))
        {
            scale = StatementScale.Of(text);
            _scaleByText[text] = scale;
        }

        return scale;
    }

    public IReadOnlyList<Quantity> QuantitiesOf(string text)
    {
        if (!_quantitiesByText.TryGetValue(text, out IReadOnlyList<Quantity>? quantities))
        {
            quantities = UnitExtractor.Extract(text);
            _quantitiesByText[text] = quantities;
        }

        return quantities;
    }
}

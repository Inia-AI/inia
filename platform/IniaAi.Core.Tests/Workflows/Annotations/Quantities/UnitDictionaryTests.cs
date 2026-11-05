using IniaAi.Core.Workflows.Annotations.Quantities;

namespace IniaAi.Core.Tests.Workflows.Annotations;

public class UnitDictionaryTests
{
    [Fact]
    public void Instance_ReturnsSameInstanceAcrossCalls()
    {
        UnitDictionary first = UnitDictionary.Instance;
        UnitDictionary second = UnitDictionary.Instance;

        Assert.Same(first, second);
    }

    [Fact]
    public void Instance_LoadsExpectedNumberOfEntries()
    {
        UnitDictionary dictionary = UnitDictionary.Instance;

        // CLDR entries (~1102) + ISO codes + magnitude scalars + supplemental.
        Assert.True(
            dictionary.Entries.Count >= 900,
            $"Expected ≥900 lexicon entries, got {dictionary.Entries.Count}");
    }

    [Fact]
    public void Instance_ContainsCommonCurrencies()
    {
        UnitDictionary dictionary = UnitDictionary.Instance;

        Assert.Contains(dictionary.Entries, e => e.Subtype == "USD");
        Assert.Contains(dictionary.Entries, e => e.Subtype == "EUR");
        Assert.Contains(dictionary.Entries, e => e.Subtype == "GBP");
    }

    [Fact]
    public void Instance_ContainsCommonSiUnits()
    {
        UnitDictionary dictionary = UnitDictionary.Instance;

        Assert.Contains(dictionary.Entries, e => e.Subtype == "mass-kilogram");
        Assert.Contains(dictionary.Entries, e => e.Subtype == "temperature-celsius");
        Assert.Contains(dictionary.Entries, e => e.Subtype == "concentr-percent");
    }

    [Fact]
    public void Instance_ContainsMagnitudeScalars()
    {
        UnitDictionary dictionary = UnitDictionary.Instance;

        Assert.Contains(dictionary.Entries, e =>
            e.Surface == "billion" && e.Kind is UnitKind.Magnitude && e.MagnitudeFactor == 1e9);
        Assert.Contains(dictionary.Entries, e =>
            e.Surface == "million" && e.Kind is UnitKind.Magnitude && e.MagnitudeFactor == 1e6);
        Assert.Contains(dictionary.Entries, e =>
            e.Surface == "trillion" && e.Kind is UnitKind.Magnitude && e.MagnitudeFactor == 1e12);
    }

    [Fact]
    public void Instance_ContainsSupplementalFinanceVocab()
    {
        UnitDictionary dictionary = UnitDictionary.Instance;

        Assert.Contains(dictionary.Entries, e =>
            e.Surface == "basis points" && e.Subtype == "concentr-basis-point");
        Assert.Contains(dictionary.Entries, e =>
            e.Surface == "dollars" && e.Subtype == "USD");
    }

    [Fact]
    public void Instance_TrieIsQueryable_FindsCommonHits()
    {
        UnitDictionary dictionary = UnitDictionary.Instance;
        var hits = new List<UnitEntry>();

        dictionary.Trie.ParseText("$300 billion", h => hits.Add(h.Value));

        Assert.Contains(hits, e => e.Subtype == "USD");
        Assert.Contains(hits, e => e.Kind is UnitKind.Magnitude && e.MagnitudeFactor == 1e9);
    }
}

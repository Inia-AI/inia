using IniaAi.Core.Workflows.Annotations.Quantities;
using System.Text.Json;

namespace IniaAi.Core.Tests.Workflows.Annotations;

public class UnitConversionTableTests
{
    [Fact]
    public void Instance_ReturnsSameInstanceAcrossCalls()
    {
        Assert.Same(UnitConversionTable.Instance, UnitConversionTable.Instance);
    }

    [Fact]
    public void Count_PinsTheGeneratedResource()
    {
        // 188 dictionary units − 3 excluded (temperature-generic, plus
        // mass-dalton and mass-solar-mass whose rationals exceed decimal
        // range) + 5 supplemental subtypes + USD + USD-cent. A regeneration
        // that changes this count must update this test deliberately.
        Assert.Equal(192, UnitConversionTable.Instance.Count);
    }

    [Fact]
    public void TryGet_Megatonne_IsBillionKilograms()
    {
        Assert.True(UnitConversionTable.Instance.TryGet("mass-megatonne", out UnitConversionEntry? entry));
        Assert.Equal("kilogram^1", entry.Dimension);
        Assert.Equal(1_000_000_000m, entry.FactorNumerator / entry.FactorDenominator);
        Assert.False(entry.HasOffset);
        Assert.Null(entry.CurrencyCode);
    }

    [Fact]
    public void TryGet_CentAndDollar_ShareTheCurrencyFamily()
    {
        Assert.True(UnitConversionTable.Instance.TryGet("USD-cent", out UnitConversionEntry? cent));
        Assert.True(UnitConversionTable.Instance.TryGet("USD", out UnitConversionEntry? dollar));
        Assert.Equal(dollar.Dimension, cent.Dimension);
        Assert.Equal("USD", cent.CurrencyCode);
        Assert.Equal("USD", dollar.CurrencyCode);
        Assert.Equal(0.01m, cent.FactorNumerator / cent.FactorDenominator);
    }

    [Fact]
    public void TryGet_Fahrenheit_CarriesTheAffineOffset()
    {
        Assert.True(UnitConversionTable.Instance.TryGet("temperature-fahrenheit", out UnitConversionEntry? entry));
        Assert.True(entry.HasOffset);
        Assert.Equal(5m, entry.FactorNumerator);
        Assert.Equal(9m, entry.FactorDenominator);
    }

    [Fact]
    public void TryGet_ExcludedOrUnknownSubtype_ReturnsFalse()
    {
        Assert.False(UnitConversionTable.Instance.TryGet("temperature-generic", out _));
        Assert.False(UnitConversionTable.Instance.TryGet("no-such-unit", out _));
        Assert.False(UnitConversionTable.Instance.TryGet(null, out _));
    }

    [Fact]
    public void ReadRational_NegativeValue_Parses()
    {
        // Python normalizes a negative rational's sign onto the numerator; a
        // CLDR tag with a negative offset must load, not poison the lazy
        // singleton with a cached FormatException.
        using JsonDocument document = JsonDocument.Parse("""{"offsetNum":"-160"}""");

        Assert.Equal(-160m, UnitConversionTable.ReadRational(document.RootElement, "offsetNum", "test-entry"));
    }
}

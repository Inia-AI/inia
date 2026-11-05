using IniaAi.Core.Workflows.Annotations.Quantities;

namespace IniaAi.Core.Tests.Workflows.Annotations;

public class QuantityTests
{
    [Fact]
    public void Quantity_RecordEquality_HoldsForSameFieldValues()
    {
        var a = new Quantity(
            Kind: UnitKind.Currency,
            Subtype: "USD",
            Value: 60.9,
            Multiplier: 1e9,
            BaseValue: 60.9e9,
            Surface: "$60.9 billion",
            Start: 0,
            End: 13);
        var b = new Quantity(
            Kind: UnitKind.Currency,
            Subtype: "USD",
            Value: 60.9,
            Multiplier: 1e9,
            BaseValue: 60.9e9,
            Surface: "$60.9 billion",
            Start: 0,
            End: 13);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Quantity_RecordEquality_DiffersWhenKindDiffers()
    {
        var currency = new Quantity(
            Kind: UnitKind.Currency, Subtype: "USD",
            Value: 300, Multiplier: 1e9, BaseValue: 3e11,
            Surface: "$300 billion", Start: 0, End: 12);
        var temperature = new Quantity(
            Kind: UnitKind.Temperature, Subtype: "temperature-kelvin",
            Value: 300, Multiplier: 1e9, BaseValue: 3e11,
            Surface: "300 billion K", Start: 0, End: 13);

        Assert.NotEqual(currency, temperature);
    }

    [Fact]
    public void Quantity_PlainKind_HasNullSubtype()
    {
        var plain = new Quantity(
            Kind: UnitKind.Plain, Subtype: null,
            Value: 2024, Multiplier: 1.0, BaseValue: 2024,
            Surface: "2024", Start: 0, End: 4);

        Assert.Equal(UnitKind.Plain, plain.Kind);
        Assert.Null(plain.Subtype);
        Assert.Equal(1.0, plain.Multiplier);
    }

    [Fact]
    public void Quantity_BaseValue_ReflectsValueTimesMultiplier()
    {
        var q = new Quantity(
            Kind: UnitKind.Currency, Subtype: "USD",
            Value: 1.2, Multiplier: 1e12, BaseValue: 1.2e12,
            Surface: "USD 1.2 trillion", Start: 0, End: 16);

        Assert.Equal(1.2e12, q.BaseValue);
    }
}

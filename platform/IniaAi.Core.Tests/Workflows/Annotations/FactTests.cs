using IniaAi.Core.Workflows.Annotations;
using IniaAi.Core.Workflows.Annotations.Quantities;
using IniaAi.Domain.Entities.Citations.Sources;

namespace IniaAi.Core.Tests.Workflows.Annotations;

public class FactTests
{
    [Fact]
    public void Constructor_SetsValueProperty()
    {
        // Arrange & Act
        var fact = new Fact("test value");

        // Assert
        Assert.Equal("test value", fact.Value);
    }

    [Fact]
    public void Constructor_InitializesEmptySourcesList()
    {
        // Arrange & Act
        var fact = new Fact("test value");

        // Assert
        Assert.NotNull(fact.Sources);
        Assert.Empty(fact.Sources);
    }

    [Fact]
    public void Constructor_LeavesQuantityNullAndNotNumerical_ByDefault()
    {
        var fact = new Fact("$5M");

        Assert.Null(fact.Quantity);
        Assert.False(fact.IsNumerical);
    }

    [Fact]
    public void IsNumericalSet_PopulatesQuantity_WhenValueIsExtractable()
    {
        var fact = new Fact("$60.9 billion") { IsNumerical = true };

        Assert.True(fact.IsNumerical);
        Assert.NotNull(fact.Quantity);
        Assert.Equal(UnitKind.Currency, fact.Quantity.Kind);
        Assert.Equal("USD", fact.Quantity.Subtype);
        Assert.Equal(60.9e9, fact.Quantity.BaseValue, 1);
    }

    [Fact]
    public void IsNumericalSet_LeavesQuantityNull_WhenValueIsNotExtractable()
    {
        // No digits in Value — extractor finds nothing, IsNumerical still set
        // because the caller explicitly marked it. Matcher will fall back to
        // substring when Quantity is null.
        var fact = new Fact("a few percent") { IsNumerical = true };

        Assert.True(fact.IsNumerical);
        Assert.Null(fact.Quantity);
    }

    [Fact]
    public void IsNumericalSet_LeavesQuantityNull_WhenValueIsBroaderThanSingleQuantity()
    {
        // Broad fact text that happens to mention a number — not a single
        // quantity claim. Quantity should NOT be populated; the Fact represents
        // the broader sentence, and its sub-numerical-facts (created by
        // FactExtractor) are the ones that get a Quantity.
        var fact = new Fact("AES had revenues of $60.9 billion in fiscal year 2024")
        {
            IsNumerical = true,
        };

        Assert.Null(fact.Quantity);
    }

    [Fact]
    public void IsNumericalSet_IsIdempotent_DoesNotOverwriteExistingQuantity()
    {
        var fact = new Fact("$60.9 billion") { IsNumerical = true };
        Quantity? first = fact.Quantity;

        fact.IsNumerical = true;

        Assert.Same(first, fact.Quantity);
    }

    [Fact]
    public void IsNumericalSet_PopulatesPercentQuantity()
    {
        var fact = new Fact("88.7%") { IsNumerical = true };

        Assert.NotNull(fact.Quantity);
        Assert.Equal(UnitKind.Concentration, fact.Quantity.Kind);
        Assert.Equal("concentr-percent", fact.Quantity.Subtype);
        Assert.Equal(88.7, fact.Quantity.Value, 0.001);
    }

    [Fact]
    public void IsNumericalSet_PopulatesPlainQuantity_WhenValueIsBareNonYearNumber()
    {
        var fact = new Fact("47") { IsNumerical = true };

        Assert.NotNull(fact.Quantity);
        Assert.Equal(UnitKind.Plain, fact.Quantity.Kind);
        Assert.Null(fact.Quantity.Subtype);
        Assert.Equal(47, fact.Quantity.Value);
    }

    [Fact]
    public void IsNumericalSet_PopulatesYearQuantity_WhenValueIsFourDigitYearInRange()
    {
        // 4-digit integers in [1900, 2100] become Year so distinct calendar
        // years stop colliding under wildcard tolerance.
        var fact = new Fact("2024") { IsNumerical = true };

        Assert.NotNull(fact.Quantity);
        Assert.Equal(UnitKind.Year, fact.Quantity.Kind);
        Assert.Null(fact.Quantity.Subtype);
        Assert.Equal(2024, fact.Quantity.Value);
    }

    [Fact]
    public void Equals_SameValue_ReturnsTrue()
    {
        // Arrange
        var fact1 = new Fact("test value");
        var fact2 = new Fact("test value");

        // Act
        bool result = fact1.Equals(fact2);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Equals_SameValueDifferentSources_ReturnsTrue()
    {
        // Arrange
        var fact1 = new Fact("test value");
        fact1.Sources.Add(new DocumentSource { CitedText = "cited text", DocumentId = "doc1", PageId = "page1" });

        var fact2 = new Fact("test value");
        fact2.Sources.Add(new UrlSource { CitedText = "different cited text", Url = "https://example.com" });

        // Act
        bool result = fact1.Equals(fact2);

        // Assert - Facts are equal if Value matches, regardless of Sources
        Assert.True(result);
    }

    [Fact]
    public void Equals_DifferentValue_ReturnsFalse()
    {
        // Arrange
        var fact1 = new Fact("value 1");
        var fact2 = new Fact("value 2");

        // Act
        bool result = fact1.Equals(fact2);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Equals_DifferentType_ReturnsFalse()
    {
        // Arrange
        var fact = new Fact("test value");
        string differentObject = "not a fact";

        // Act
        bool result = fact.Equals(differentObject);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GetHashCode_SameValue_ReturnsSameHash()
    {
        // Arrange
        var fact1 = new Fact("test value");
        var fact2 = new Fact("test value");

        // Act
        int hash1 = fact1.GetHashCode();
        int hash2 = fact2.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void GetHashCode_DifferentValue_ReturnsDifferentHash()
    {
        // Arrange
        var fact1 = new Fact("value 1");
        var fact2 = new Fact("value 2");

        // Act
        int hash1 = fact1.GetHashCode();
        int hash2 = fact2.GetHashCode();

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    #region AddSource Tests

    [Fact]
    public void AddSource_NewSource_AddsToList()
    {
        var fact = new Fact("$5M");
        var source = new DocumentSource { CitedText = "revenue $5M", DocumentId = "d1", PageId = "p1" };

        fact.AddSource(source);

        _ = Assert.Single(fact.Sources);
        Assert.Same(source, fact.Sources[0]);
    }

    [Fact]
    public void AddSource_DuplicateSourceKey_SameCitedText_DoesNotAdd()
    {
        var fact = new Fact("$5M");
        var source1 = new DocumentSource { CitedText = "revenue $5M", DocumentId = "d1", PageId = "p1" };
        var source2 = new DocumentSource { CitedText = "revenue $5M", DocumentId = "d1", PageId = "p1" };

        fact.AddSource(source1);
        fact.AddSource(source2);

        _ = Assert.Single(fact.Sources);
    }

    [Fact]
    public void AddSource_DuplicateSourceKey_NeitherContainsValue_UsesPlaceholder()
    {
        // Neither CitedText contains the fact value — fall back to placeholder
        var fact = new Fact("$5M");
        var source1 = new DocumentSource { CitedText = "first citation", DocumentId = "d1", PageId = "p1" };
        var source2 = new DocumentSource { CitedText = "second citation", DocumentId = "d1", PageId = "p1" };

        fact.AddSource(source1);
        fact.AddSource(source2);

        _ = Assert.Single(fact.Sources);
        Assert.Equal(Fact.MULTIPLE_CITATIONS_PLACEHOLDER, fact.Sources[0].CitedText);
    }

    [Fact]
    public void AddSource_DuplicateSourceKey_FoldNeverWritesThroughTheCallersInstances()
    {
        var fact = new Fact("$5M");
        var source1 = new DocumentSource { CitedText = "first citation", DocumentId = "d1", PageId = "p1" };
        var source2 = new DocumentSource { CitedText = "second citation", DocumentId = "d1", PageId = "p1" };

        fact.AddSource(source1);
        fact.AddSource(source2);

        Assert.Equal(Fact.MULTIPLE_CITATIONS_PLACEHOLDER, Assert.Single(fact.Sources).CitedText);
        Assert.Equal("first citation", source1.CitedText);
        Assert.Equal("second citation", source2.CitedText);
    }

    [Fact]
    public void AddSource_DuplicateSourceKey_ExistingContainsValue_KeepsExisting()
    {
        // Existing CitedText contains the value — keep it
        var fact = new Fact("$5M");
        var source1 = new DocumentSource { CitedText = "revenue was $5M", DocumentId = "d1", PageId = "p1" };
        var source2 = new DocumentSource { CitedText = "financial summary", DocumentId = "d1", PageId = "p1" };

        fact.AddSource(source1);
        fact.AddSource(source2);

        _ = Assert.Single(fact.Sources);
        Assert.Equal("revenue was $5M", fact.Sources[0].CitedText);
    }

    [Fact]
    public void AddSource_DuplicateSourceKey_NewContainsValue_ReplacesWithNew()
    {
        // New CitedText contains the value but existing doesn't — replace
        var fact = new Fact("$5M");
        var source1 = new DocumentSource { CitedText = "financial summary", DocumentId = "d1", PageId = "p1" };
        var source2 = new DocumentSource { CitedText = "revenue was $5M", DocumentId = "d1", PageId = "p1" };

        fact.AddSource(source1);
        fact.AddSource(source2);

        _ = Assert.Single(fact.Sources);
        Assert.Equal("revenue was $5M", fact.Sources[0].CitedText);
    }

    [Fact]
    public void AddSource_DuplicateSourceKey_BothContainValue_KeepsExisting()
    {
        // Both contain the value — keep existing (first wins)
        var fact = new Fact("$5M");
        var source1 = new DocumentSource { CitedText = "revenue $5M in Q4", DocumentId = "d1", PageId = "p1" };
        var source2 = new DocumentSource { CitedText = "$5M annual report", DocumentId = "d1", PageId = "p1" };

        fact.AddSource(source1);
        fact.AddSource(source2);

        _ = Assert.Single(fact.Sources);
        Assert.Equal("revenue $5M in Q4", fact.Sources[0].CitedText);
    }

    [Fact]
    public void AddSource_DifferentSourceKeys_AddsBoth()
    {
        var fact = new Fact("$5M");
        var doc = new DocumentSource { CitedText = "revenue $5M", DocumentId = "d1", PageId = "p1" };
        var url = new UrlSource { CitedText = "$5M filing", Url = "https://example.com" };

        fact.AddSource(doc);
        fact.AddSource(url);

        Assert.Equal(2, fact.Sources.Count);
    }

    [Fact]
    public void AddSource_BlockSourceDuplicate_NewContainsValue_Replaces()
    {
        var fact = new Fact("$5M");
        var block1 = new BlockSource { CitedText = "block summary", BlockId = "b1" };
        var block2 = new BlockSource { CitedText = "revenue $5M from block", BlockId = "b1" };

        fact.AddSource(block1);
        fact.AddSource(block2);

        _ = Assert.Single(fact.Sources);
        Assert.Equal("revenue $5M from block", fact.Sources[0].CitedText);
    }

    [Fact]
    public void AddSource_DuplicateSourceKey_NewCitedTextHasQuantityEquivalent_ReplacesWithNew()
    {
        // Tier 2 quantity-equivalent: CitedText doesn't literally contain the
        // fact value, but extracted quantity matches within tolerance.
        // "$184 billion" ≈ "183.99 billion" (~0.005% drift) via Plain
        // wildcard. Pair drift kept tight so the test isn't sensitive to the
        // wildcard tolerance constant.
        var fact = new Fact("$184 billion") { IsNumerical = true };
        var existing = new DocumentSource { CitedText = "financial summary section", DocumentId = "d1", PageId = "p1" };
        var quantityEquivalent = new DocumentSource { CitedText = "approximately 183.99 billion in net assets", DocumentId = "d1", PageId = "p1" };

        fact.AddSource(existing);
        fact.AddSource(quantityEquivalent);

        _ = Assert.Single(fact.Sources);
        Assert.Equal("approximately 183.99 billion in net assets", fact.Sources[0].CitedText);
    }

    [Fact]
    public void AddSource_DuplicateSourceKey_ExistingHasQuantityEquivalent_KeepsExisting()
    {
        // Mirror of the above: existing CitedText has the quantity-equivalent
        // surface; new is generic. Keep existing.
        var fact = new Fact("$184 billion") { IsNumerical = true };
        var existing = new DocumentSource { CitedText = "approximately 183.99 billion in net assets", DocumentId = "d1", PageId = "p1" };
        var generic = new DocumentSource { CitedText = "financial summary section", DocumentId = "d1", PageId = "p1" };

        fact.AddSource(existing);
        fact.AddSource(generic);

        _ = Assert.Single(fact.Sources);
        Assert.Equal("approximately 183.99 billion in net assets", fact.Sources[0].CitedText);
    }

    [Fact]
    public void AddSource_DuplicateSourceKey_QuantityKindMismatch_DoesNotMatch()
    {
        // Fact value is Currency USD 300B; CitedText mentions "300 billion Kelvin"
        // — different kind. Quantity matcher must NOT consider this a match;
        // both CitedTexts are equally non-matching → placeholder.
        var fact = new Fact("$300 billion") { IsNumerical = true };
        var unrelated1 = new DocumentSource { CitedText = "thermal extreme of 300 billion Kelvin", DocumentId = "d1", PageId = "p1" };
        var unrelated2 = new DocumentSource { CitedText = "another generic line", DocumentId = "d1", PageId = "p1" };

        fact.AddSource(unrelated1);
        fact.AddSource(unrelated2);

        _ = Assert.Single(fact.Sources);
        Assert.Equal(Fact.MULTIPLE_CITATIONS_PLACEHOLDER, fact.Sources[0].CitedText);
    }

    #endregion

    #region AddSources Tests

    [Fact]
    public void AddSources_MultipleSources_AddsAllUnique()
    {
        var fact = new Fact("$5M");
        var sources = new List<BaseSource>
        {
            new DocumentSource { CitedText = "revenue $5M", DocumentId = "d1", PageId = "p1" },
            new UrlSource { CitedText = "$5M filing", Url = "https://example.com" },
            new BlockSource { CitedText = "$5M block", BlockId = "b1" }
        };

        fact.AddSources(sources);

        Assert.Equal(3, fact.Sources.Count);
    }

    [Fact]
    public void AddSources_WithDuplicates_KeepsCitedTextContainingValue()
    {
        var fact = new Fact("$5M");
        var sources = new List<BaseSource>
        {
            new DocumentSource { CitedText = "summary without value", DocumentId = "d1", PageId = "p1" },
            new DocumentSource { CitedText = "revenue was $5M in report", DocumentId = "d1", PageId = "p1" }
        };

        fact.AddSources(sources);

        _ = Assert.Single(fact.Sources);
        Assert.Equal("revenue was $5M in report", fact.Sources[0].CitedText);
    }

    #endregion
}

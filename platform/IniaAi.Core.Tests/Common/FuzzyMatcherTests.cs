using IniaAi.Core.Common;

namespace IniaAi.Core.Tests.Common;

public class FuzzyMatcherTests
{
    [Fact]
    public void CalculateSimilarity_SourceNull_ReturnsZero()
    {
        // Arrange
        string? source = null;
        string target = "test";

        // Act
        double result = FuzzyMatcher.CalculateSimilarity(source, target);

        // Assert
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void CalculateSimilarity_TargetNull_ReturnsZero()
    {
        // Arrange
        string source = "test";
        string? target = null;

        // Act
        double result = FuzzyMatcher.CalculateSimilarity(source, target);

        // Assert
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void CalculateSimilarity_SourceEmpty_ReturnsZero()
    {
        // Arrange
        string source = "";
        string target = "test";

        // Act
        double result = FuzzyMatcher.CalculateSimilarity(source, target);

        // Assert
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void CalculateSimilarity_TargetEmpty_ReturnsZero()
    {
        // Arrange
        string source = "test";
        string target = "";

        // Act
        double result = FuzzyMatcher.CalculateSimilarity(source, target);

        // Assert
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void CalculateSimilarity_IdenticalStrings_ReturnsOne()
    {
        // Arrange
        string source = "hello world";
        string target = "hello world";

        // Act
        double result = FuzzyMatcher.CalculateSimilarity(source, target);

        // Assert
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void CalculateSimilarity_NormalizedIdenticalStrings_ReturnsOne()
    {
        // Arrange
        // Both strings normalize to "iniaai" (common suffix "inc" is removed)
        string source = "IniaAi, Inc.";
        string target = "iniaai";

        // Act
        double result = FuzzyMatcher.CalculateSimilarity(source, target);

        // Assert
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void CalculateSimilarity_ExtraSpacesAndPunctuation_ReturnsOne()
    {
        // Arrange
        string source = "Hello,   world!";
        string target = "hello world";

        // Act
        double result = FuzzyMatcher.CalculateSimilarity(source, target);

        // Assert
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void CalculateSimilarity_SimilarStrings_ReturnsCorrectSimilarity()
    {
        // Arrange
        // "hello" vs. "hallo" differ by one character.
        // Levenshtein distance = 1, max length = 5 so expected similarity = 1 - 1/5 = 0.8
        string source = "hello";
        string target = "hallo";
        double expectedSimilarity = 0.8;

        // Act
        double result = FuzzyMatcher.CalculateSimilarity(source, target);

        // Assert
        Assert.Equal(expectedSimilarity, result, 3); // 3 decimal places precision
    }

    [Fact]
    public void CalculateSimilarity_CompletelyDifferentStrings_ReturnsZero()
    {
        // Arrange
        // "abc" vs. "xyz" have a Levenshtein distance of 3, and max length = 3 so similarity = 0.
        string source = "abc";
        string target = "xyz";
        double expectedSimilarity = 0.0;

        // Act
        double result = FuzzyMatcher.CalculateSimilarity(source, target);

        // Assert
        Assert.Equal(expectedSimilarity, result);
    }
}

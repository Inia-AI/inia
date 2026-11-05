using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Domain.Tests.Data.Workflows;

public class RagSettingsExecutionStateExecutionStateTests
{
    [Fact]
    public void StartIndexInput_SetValidInput_ParsesValuesCorrectly()
    {
        // Arrange & Act
        var executionState = new RagSettingsExecutionState
        {
            StartIndexInput = "10"
        };

        // Assert
        Assert.Equal(10, executionState.StartIndex);
    }

    [Fact]
    public void EndIndexInput_SetValidInput_ParsesValuesCorrectly()
    {
        // Arrange & Act
        var executionState = new RagSettingsExecutionState
        {
            EndIndexInput = "20",
        };

        // Assert
        Assert.Equal(20, executionState.EndIndex);
    }

    [Fact]
    public void NumberOfPagesInput_SetValidInput_ParsesValuesCorrectly()
    {
        // Arrange & Act
        var executionState = new RagSettingsExecutionState
        {
            NumberOfPagesInput = "5"
        };

        // Assert
        Assert.Equal(5, executionState.NumberOfPagesToUse);
    }

    [Fact]
    public void ExactIndexesInput_SetValidInput_ParsesValuesCorrectly()
    {
        // Arrange & Act
        var executionState = new RagSettingsExecutionState
        {
            ExactIndexesInput = "1,2,3-5",
        };

        // Assert
        Assert.Equal([1, 2, 3, 4, 5], executionState.ExactIndexes);
    }

    [Fact]
    public void StartIndexInput_SetInvalidInput_ResetsToDefaultValue()
    {
        // Arrange
        var executionState = new RagSettingsExecutionState() { StartIndexInput = "2", EndIndexInput = "10", NumberOfPagesInput = "5" };

        // Act
        executionState.StartIndexInput = "20";

        // Assert
        Assert.Equal(0, executionState.StartIndex);
    }

    [Fact]
    public void EndIndexInput_SetInvalidInput_ResetsToDefaultValue()
    {
        // Arrange
        var executionState = new RagSettingsExecutionState() { StartIndexInput = "10", EndIndexInput = "20", NumberOfPagesInput = "5" };

        // Act
        executionState.EndIndexInput = "5";

        // Assert
        Assert.Equal(int.MaxValue, executionState.EndIndex);
    }

    [InlineData("1 + 1", 2)]
    [InlineData("10 - 5", 5)]
    [InlineData("2*2", 4)]
    [InlineData("10 / 2", 5)]
    [InlineData("20 * 2", 40)]
    [InlineData("5 + 5", 10)]
    [Theory]
    public void StartIndexInput_SetValidInputWithExpressions_ParsesValuesCorrectly(string startIndexInput, int startIndex)
    {
        // Arrange & Act
        var executionState = new RagSettingsExecutionState
        {
            StartIndexInput = startIndexInput
        };

        // Assert
        Assert.Equal(startIndex, executionState.StartIndex);
    }

    [InlineData("1 + 1", 2)]
    [InlineData("10 - 5", 5)]
    [InlineData("2*2", 4)]
    [InlineData("10 / 2", 5)]
    [InlineData("20 * 2", 40)]
    [InlineData("5 + 5", 10)]
    [Theory]
    public void EndIndexInput_SetValidInputWithExpressions_ParsesValuesCorrectly(string endIndexInput, int endIndex)
    {
        // Arrange & Act
        var executionState = new RagSettingsExecutionState
        {
            EndIndexInput = endIndexInput
        };

        // Assert
        Assert.Equal(endIndex, executionState.EndIndex);
    }

    [InlineData("1 + 1", 2)]
    [InlineData("10 - 5", 5)]
    [InlineData("2*2", 4)]
    [InlineData("10 / 2", 5)]
    [InlineData("20 * 2", 40)]
    [InlineData("5 + 5", 10)]
    [Theory]
    public void NumberOfPagesInput_SetValidInputWithExpressions_ParsesValuesCorrectly(string numberOfPagesInput, int numberOfPages)
    {
        // Arrange & Act
        var executionState = new RagSettingsExecutionState
        {
            NumberOfPagesInput = numberOfPagesInput
        };

        // Assert
        Assert.Equal(numberOfPages, executionState.NumberOfPagesToUse);
    }

    [Fact]
    public void ExactIndexesInput_SetValidInputWithExpressions_ParsesValuesCorrectly()
    {
        // Arrange & Act
        var executionState = new RagSettingsExecutionState
        {
            ExactIndexesInput = "1, 2, 3, 4-5, 6 -8,10",
        };

        // Assert
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 10], executionState.ExactIndexes);
    }

    [InlineData("[1,2]", new[] { 1, 2 })]
    [InlineData("[1, 2, 3]", new[] { 1, 2, 3 })]
    [InlineData("[1,2,3-5]", new[] { 1, 2, 3, 4, 5 })]
    [InlineData("[\"1\",\"2\"]", new[] { 1, 2 })]
    [InlineData(" [ 1 , 2 ] ", new[] { 1, 2 })]
    [InlineData("(1,2)", new[] { 1, 2 })]
    [InlineData("(1, 2, 3)", new[] { 1, 2, 3 })]
    [InlineData("{1,2}", new[] { 1, 2 })]
    [InlineData("{1, 2, 3-5}", new[] { 1, 2, 3, 4, 5 })]
    [InlineData("Page 7", new[] { 7 })]
    [InlineData("pages 1-3", new[] { 1, 2, 3 })]
    [InlineData("Page 1, page 2", new[] { 1, 2 })]
    [InlineData("page7", new[] { 7 })]
    [InlineData("No data available, 7", new[] { 7 })]
    [InlineData("1,abc,2", new[] { 1, 2 })]
    [InlineData("1,,2", new[] { 1, 2 })]
    [InlineData(",7", new[] { 7 })]
    [InlineData("5-3,7", new[] { 7 })]
    [InlineData("1,2,3,99999999999", new[] { 1, 2, 3 })]
    [InlineData("99999999999,5", new[] { 5 })]
    [InlineData("1-99999999999,7", new[] { 7 })]
    [Theory]
    public void ExactIndexesInput_SetTolerableNoise_ParsesValuesCorrectly(string input, int[] expected)
    {
        var executionState = new RagSettingsExecutionState
        {
            ExactIndexesInput = input,
        };

        Assert.Equal(expected, executionState.ExactIndexes);
    }

    [InlineData("-1-")]
    [InlineData("1.5")]
    [InlineData("500.50")]
    [InlineData("1abc2")]
    [InlineData("!@#")]
    [InlineData("--1")]
    [InlineData("abc")]
    [Theory]
    public void ExactIndexesInput_SetMalformedInput_ReturnsEmpty(string input)
    {
        var executionState = new RagSettingsExecutionState
        {
            ExactIndexesInput = input,
        };

        Assert.Empty(executionState.ExactIndexes);
    }

    [Fact]
    public void DynamicInput_SetPlaceHolders_DoesntThrowException()
    {
        // Arrange
        var executionState = new RagSettingsExecutionState()
        {
            StartIndexInput = "{start}",
            EndIndexInput = "{end}",
            NumberOfPagesInput = "{pages}",
            ExactIndexesInput = "{exactIndexes}",
        };

        // Act & Assert
        Assert.Equal("{start}", executionState.StartIndexInput);
        Assert.Equal("{end}", executionState.EndIndexInput);
        Assert.Equal("{pages}", executionState.NumberOfPagesInput);
        Assert.Equal("{exactIndexes}", executionState.ExactIndexesInput);
        Assert.Equal(0, executionState.StartIndex);
        Assert.Equal(int.MaxValue, executionState.EndIndex);
        Assert.Equal(1, executionState.NumberOfPagesToUse);
        Assert.Empty(executionState.ExactIndexes);
    }
}

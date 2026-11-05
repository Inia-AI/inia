using IniaAi.Core.Workflows.Graph;

namespace IniaAi.Core.Tests.Workflows.Graphs;

public class PreviewSanitizerTests
{
    [Fact]
    public void ShouldTruncateString()
    {
        // Arrange
        int truncatedLength = 3000;
        string phrase = "This is a test";
        int lenPhrase = phrase.Length;
        int targetLength = 5000;

        string longString = string.Concat(Enumerable.Repeat(phrase, targetLength / lenPhrase));

        //Act
        (string? preview, _) = PreviewSanitizer.Sanitize(longString);

        //Assert
        Assert.Equal(truncatedLength, preview?.Length);
    }
    [Fact]
    public void ShouldHandleNullInput()
    {
        // Arrange
        string? input = null;

        // Act
        (string? preview, int fullLength) = PreviewSanitizer.Sanitize(input);

        // Assert
        Assert.Null(preview);
        Assert.Equal(0, fullLength);
    }
}

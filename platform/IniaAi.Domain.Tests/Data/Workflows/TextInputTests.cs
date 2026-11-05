using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;

namespace IniaAi.Domain.Tests.Data.Workflows;

public class TextInputTests
{
    [Fact]
    public void Constructor_WithBlockAndReplacementTag_SetsProperties()
    {
        // Arrange
        var block = new Block();
        string replacementTag = "tag";

        // Act
        var textInput = new TextInput(block, replacementTag);

        // Assert
        Assert.Equal(block, textInput.Block);
        Assert.Equal(replacementTag, textInput.ReplacementTag);
        Assert.Null(textInput.Value);
        Assert.Equal(block.Id, textInput.BlockId);
    }

    [Fact]
    public void Constructor_WithBlockReplacementTagAndValue_SetsProperties()
    {
        // Arrange
        var block = new Block();
        string replacementTag = "tag";
        string value = "value";

        // Act
        var textInput = new TextInput(block, replacementTag, value);

        // Assert
        Assert.Equal(block, textInput.Block);
        Assert.Equal(replacementTag, textInput.ReplacementTag);
        Assert.Equal(value, textInput.Value);
        Assert.Equal(block.Id, textInput.BlockId);
    }

    [Fact]
    public void CloneToBlock_WithBlock_CreatesClonedTextInput()
    {
        // Arrange
        var block = new Block();
        string replacementTag = "tag";
        string value = "value";
        var newBlock = new Block();
        var textInput = new TextInput(newBlock, replacementTag, value);

        // Act
        TextInput clonedTextInput = textInput.CloneToBlock(block);

        // Assert
        Assert.Equal(block, clonedTextInput.Block);
        Assert.Equal(replacementTag, clonedTextInput.ReplacementTag);
        Assert.Equal(value, clonedTextInput.Value);
        Assert.Equal(block.Id, clonedTextInput.BlockId);
    }

    [Fact]
    public void Equals_WithDifferentReplacementTag_ReturnsFalse()
    {
        // Arrange
        var textInput1 = new TextInput(new Block(), "tag1");
        var textInput2 = new TextInput(new Block(), "tag2");

        // Act
        bool result = textInput1.Equals(textInput2);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void FromJson_ValidJson_ReturnsTextInput()
    {
        // Arrange
        string json = @"{
            ""BlockId"": ""test-block-id"",
            ""ReplacementTag"": ""test-replacement-tag"",
            ""Value"": ""test-value""
        }";

        // Act
        var textInput = TextInput.FromJson(json);

        // Assert
        Assert.NotNull(textInput);
        Assert.Equal("test-replacement-tag", textInput.ReplacementTag);
        Assert.Equal("test-value", textInput.Value);
    }
}

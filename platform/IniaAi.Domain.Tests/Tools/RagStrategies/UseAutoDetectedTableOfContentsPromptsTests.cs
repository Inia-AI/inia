using IniaAi.Domain.Tools.RagStrategies;

namespace IniaAi.Domain.Tests.Tools.RagStrategies;

public class UseAutoDetectedTableOfContentsPromptsTests
{
    [Fact]
    public void ToJson_ValidObject_ReturnsJsonString()
    {
        // Arrange
        var prompts = new UseAutoDetectedTableOfContentsPrompts();

        // Act
        string json = prompts.ToJson(pretty: true);

        // Assert
        Assert.NotNull(json);
        Assert.NotEmpty(json);
        Assert.Contains(nameof(prompts.RelevantSectionsIdentificationPrompt), json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(prompts.PageSelectionPrompt), json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(prompts.RelevanceAssessmentPrompt), json, StringComparison.OrdinalIgnoreCase);
    }
}

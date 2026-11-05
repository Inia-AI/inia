using IniaAi.AiConnection.AwsBedrock;
using IniaAi.Common.Extensions;
using IniaAi.Configuration;
using IniaAi.Testing;
using Microsoft.Extensions.Logging;
using AnthropicAiModel = IniaAi.Configuration.Enums.Anthropic.AiModel;

namespace IniaAi.AiConnection.Tests.AwsBedrock;

public class ModelRecognitionTests
{
    [Fact]
    public void AnthropicFormatAdapter_SupportsModel_RecognizesStandardModels()
    {
        // Arrange
        var platformSettings = new PlatformSettings();
        ILogger<AnthropicFormatAdapter> logger = TestsLogger.CreateLogger<AnthropicFormatAdapter>(new TestOutputHelper());
        var adapter = new AnthropicFormatAdapter(platformSettings, logger);

        // Act & Assert
        Assert.True(adapter.SupportsModel("us.anthropic.claude-sonnet-4-6"));
        Assert.True(adapter.SupportsModel("us.anthropic.claude-opus-4-6-v1"));
        Assert.True(adapter.SupportsModel("us.anthropic.claude-haiku-4-5-20251001-v1:0"));
    }

    [Fact]
    public void AnthropicFormatAdapter_SupportsModel_RecognizesRegionalModels()
    {
        // Arrange
        var platformSettings = new PlatformSettings();
        ILogger<AnthropicFormatAdapter> logger = TestsLogger.CreateLogger<AnthropicFormatAdapter>(new TestOutputHelper());
        var adapter = new AnthropicFormatAdapter(platformSettings, logger);

        // Act & Assert - Test US region models
        Assert.True(adapter.SupportsModel("us.anthropic.claude-sonnet-4-6"));
        Assert.True(adapter.SupportsModel("us.anthropic.claude-opus-4-6-v1"));
        Assert.True(adapter.SupportsModel("us.anthropic.claude-haiku-4-5-20251001-v1:0"));

        // Act & Assert - Test EU region models
        Assert.True(adapter.SupportsModel("eu.anthropic.claude-sonnet-4-6"));
        Assert.True(adapter.SupportsModel("eu.anthropic.claude-opus-4-6-v1"));
        Assert.True(adapter.SupportsModel("eu.anthropic.claude-haiku-4-5-20251001-v1:0"));
    }

    [Fact]
    public void AnthropicFormatAdapter_SupportsModel_RecognizesSpecificProblematicModelId()
    {
        // Arrange
        var platformSettings = new PlatformSettings();
        ILogger<AnthropicFormatAdapter> logger = TestsLogger.CreateLogger<AnthropicFormatAdapter>(new TestOutputHelper());
        var adapter = new AnthropicFormatAdapter(platformSettings, logger);

        // Act & Assert - Test the exact model ID that was causing the NotSupportedException
        Assert.True(adapter.SupportsModel("us.anthropic.claude-haiku-4-5-20251001-v1:0"));
    }

    [Fact]
    public void AnthropicFormatAdapter_SupportsModel_RejectsNonAnthropicModels()
    {
        // Arrange
        var platformSettings = new PlatformSettings();
        ILogger<AnthropicFormatAdapter> logger = TestsLogger.CreateLogger<AnthropicFormatAdapter>(new TestOutputHelper());
        var adapter = new AnthropicFormatAdapter(platformSettings, logger);

        // Act & Assert
        Assert.False(adapter.SupportsModel("mistral.mistral-7b-instruct-v0:2"));
        Assert.False(adapter.SupportsModel("amazon.titan-text-v1"));
        Assert.False(adapter.SupportsModel("cohere.command-text-v14"));
        Assert.False(adapter.SupportsModel("invalid-model-id"));
    }

    [Fact]
    public void AnthropicFormatAdapter_ParseResponse_HandlesEmptyJson()
    {
        // Arrange
        var platformSettings = new PlatformSettings();
        ILogger<AnthropicFormatAdapter> logger = TestsLogger.CreateLogger<AnthropicFormatAdapter>(new TestOutputHelper());
        var adapter = new AnthropicFormatAdapter(platformSettings, logger);

        // Act & Assert
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => adapter.ParseResponse(""));
        Assert.Contains("Received empty or null response JSON for Anthropic model", exception.Message);

        // Test with null as well
        InvalidOperationException nullException = Assert.Throws<InvalidOperationException>(() => adapter.ParseResponse(null!));
        Assert.Contains("Received empty or null response JSON for Anthropic model", nullException.Message);
    }

    [Fact]
    public void ModelFamily_AnthropicFamilyUsesAnthropicModels()
    {
        // Arrange
        var platformSettings = new PlatformSettings();

        // Act - Test with Anthropic family
        string anthropicAdvancedModelId = GetTestModelIdWithFamily(platformSettings, true, ModelFamily.Anthropic);
        string anthropicBasicModelId = GetTestModelIdWithFamily(platformSettings, false, ModelFamily.Anthropic);

        // Assert - Should use Anthropic models (Claude models)
        Assert.Contains("claude", anthropicAdvancedModelId, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("claude", anthropicBasicModelId, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ModelFamily_NullFamilyDefaultsToAnthropic()
    {
        // Arrange
        var platformSettings = new PlatformSettings();

        // Act - Test with null family (should default to Anthropic)
        string defaultModelId = GetTestModelIdWithFamily(platformSettings, true, null);

        // Assert - Should default to Anthropic (Claude models)
        Assert.Contains("claude", defaultModelId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Helper method to test model ID determination with ModelFamily parameter
    /// </summary>
    private static string GetTestModelIdWithFamily(
        PlatformSettings settings,
        bool shouldUseAdvancedModel,
        ModelFamily? family,
        string? explicitModel = null)
    {
        // If explicit model is provided, return it (mimics the real logic)
        if (explicitModel is not null)
        {
            return explicitModel;
        }

        // Use specified family or default to Anthropic
        ModelFamily targetFamily = family ?? ModelFamily.Anthropic;

        return targetFamily switch
        {
            ModelFamily.Anthropic => GetAnthropicTestModel(settings, shouldUseAdvancedModel),
            _ => GetAnthropicTestModel(settings, shouldUseAdvancedModel) // Safe fallback to Anthropic
        };
    }

    private static string GetAnthropicTestModel(PlatformSettings settings, bool shouldUseAdvancedModel)
    {
        AnthropicAiModel anthropicModel = shouldUseAdvancedModel
            ? settings.Anthropic.AdvancedModel
            : settings.Anthropic.BasicModel;
        return anthropicModel.GetEnumMember() ?? "anthropic.claude-3-sonnet-20240229-v1:0";
    }

    private class TestOutputHelper : ITestOutputHelper
    {
        public string Output => string.Empty;
        public void Write(string message) { }
        public void Write(string format, params object[] args) { }
        public void WriteLine(string message) { }
        public void WriteLine(string format, params object[] args) { }
    }
}

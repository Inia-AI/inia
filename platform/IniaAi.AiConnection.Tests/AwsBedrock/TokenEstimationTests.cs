using IniaAi.AiConnection.Anthropic.ApiModel;
using IniaAi.AiConnection.AwsBedrock;
using IniaAi.AiConnection.Common.ChatConnector.Enums;
using IniaAi.Common.Content;
using IniaAi.Common.Enums;
using IniaAi.Configuration;
using IniaAi.Testing;
using Microsoft.Extensions.Logging;

namespace IniaAi.AiConnection.Tests.AwsBedrock;

/// <summary>
/// Tests for token estimation accuracy in format adapters.
/// These tests verify that the new EstimateTokensFromMessage method provides
/// accurate estimates for multimodal content (text + images).
/// </summary>
public class TokenEstimationTests
{
    private readonly ITestOutputHelper _output;
    private readonly PlatformSettings _platformSettings;

    public TokenEstimationTests(ITestOutputHelper output)
    {
        _output = output;

        // Create minimal platform settings for testing
        _platformSettings = new PlatformSettings
        {
            Anthropic = new Configuration.LLMProviders.AnthropicSettings(),
        };
    }

    [Fact]
    public void AnthropicAdapter_EstimateTokensFromMessage_TextOnly_ReturnsReasonableEstimate()
    {
        // Arrange
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestsLoggerProvider(_output)));
        ILogger<AnthropicFormatAdapter> logger = loggerFactory.CreateLogger<AnthropicFormatAdapter>();
        var adapter = new AnthropicFormatAdapter(_platformSettings, logger);

        var message = new Message(new ContentRequest(new("This is a simple test message with some text content.")));

        // Act
        int tokens = adapter.EstimateTokensFromMessage(message, null);

        // Assert
        _output.WriteLine($"Estimated tokens for text-only message: {tokens}");
        Assert.True(tokens > 0, "Token count should be greater than 0");
        Assert.True(tokens < 1000, "Token count should be reasonable for short text (less than 1000)");
    }

    [Fact]
    public void AnthropicAdapter_EstimateTokensFromMessage_WithSystemPrompt_IncludesSystemPromptTokens()
    {
        // Arrange
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestsLoggerProvider(_output)));
        ILogger<AnthropicFormatAdapter> logger = loggerFactory.CreateLogger<AnthropicFormatAdapter>();
        var adapter = new AnthropicFormatAdapter(_platformSettings, logger);

        var message = new Message(new ContentRequest(new("User message")));
        string systemPrompt = "You are a helpful assistant with expertise in analyzing documents.";

        // Act
        int tokensWithoutSystem = adapter.EstimateTokensFromMessage(message, null);
        int tokensWithSystem = adapter.EstimateTokensFromMessage(message, systemPrompt);

        // Assert
        _output.WriteLine($"Tokens without system prompt: {tokensWithoutSystem}");
        _output.WriteLine($"Tokens with system prompt: {tokensWithSystem}");
        Assert.True(tokensWithSystem > tokensWithoutSystem,
            "Token count should be higher when system prompt is included");
    }

    [Fact]
    public void AnthropicAdapter_EstimateTokensFromMessage_WithImages_CountsImageTokensCorrectly()
    {
        // Arrange
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestsLoggerProvider(_output)));
        ILogger<AnthropicFormatAdapter> logger = loggerFactory.CreateLogger<AnthropicFormatAdapter>();
        var adapter = new AnthropicFormatAdapter(_platformSettings, logger);

        // Create a message with text and 3 images
        var message = new Message(new ContentRequest(new("Please analyze these images.")));

        // Add 3 test images (small 1x1 pixel images)
        byte[] testImageBytes = CreateSmallTestImage();
        for (int i = 0; i < 3; i++)
        {
            message.Content.Add(new(new(new BinaryContent(testImageBytes, MediaType.ImagePng), CommonContentType.Image)));
        }

        // Act
        int tokens = adapter.EstimateTokensFromMessage(message, null);

        // Assert
        _output.WriteLine($"Estimated tokens for message with 3 images: {tokens}");

        // Expected: ~1568 tokens per image × 3 images = ~4,704 tokens + text + overhead
        // Should be in the range of 4,500 - 6,000 tokens
        Assert.True(tokens > 4000, $"Token count should include image tokens (expected >4000, got {tokens})");
        Assert.True(tokens < 8000, $"Token count should be reasonable for 3 images + text (expected <8000, got {tokens})");
    }

    [Fact]
    public void AnthropicAdapter_EstimateTokensFromMessage_DoesNotTokenizeBase64()
    {
        // Arrange
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestsLoggerProvider(_output)));
        ILogger<AnthropicFormatAdapter> logger = loggerFactory.CreateLogger<AnthropicFormatAdapter>();
        var adapter = new AnthropicFormatAdapter(_platformSettings, logger);

        // Create a message with 1 image
        var message = new Message(new ContentRequest(new("Analyze this image.")));
        byte[] largeImageBytes = new byte[1024 * 1024]; // 1MB of data
        Array.Fill<byte>(largeImageBytes, 0xFF);
        message.Content.Add(new(new(new BinaryContent(largeImageBytes, MediaType.ImageJpeg), CommonContentType.Image)));

        // Act
        int tokens = adapter.EstimateTokensFromMessage(message, null);

        // Assert
        _output.WriteLine($"Estimated tokens for message with 1MB image: {tokens}");

        // Expected: ~1568 tokens for the image (NOT tokenizing base64 string)
        // If we were tokenizing base64, we'd get ~350,000+ tokens
        Assert.True(tokens < 5000,
            $"Token count should use fixed image cost, not tokenize base64 (expected <5000, got {tokens})");
    }

    // Helper methods

    private static byte[] CreateSmallTestImage()
    {
        // Create a minimal PNG-like structure (1x1 pixel)
        return [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    }
}

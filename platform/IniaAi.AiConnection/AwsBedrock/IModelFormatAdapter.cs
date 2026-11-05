using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Common.Enums;

namespace IniaAi.AiConnection.AwsBedrock;

/// <summary>
/// Defines the contract for adapting requests and responses for different model families on AWS Bedrock.
/// </summary>
internal interface IModelFormatAdapter
{
    /// <summary>
    /// Gets the model family this adapter handles.
    /// </summary>
    ModelFamily ModelFamily { get; }

    /// <summary>
    /// Creates a request body for the model family from a text input.
    /// </summary>
    /// <param name="userInput">The user input text</param>
    /// <param name="systemPrompt">Optional system prompt</param>
    /// <param name="maxTokens">Maximum tokens to generate</param>
    /// <param name="temperature">Temperature for generation</param>
    /// <param name="modelId">The specific model ID to use</param>
    /// <param name="effort">The effort level for adaptive-generation models; ignored by models without effort support</param>
    /// <returns>JSON string representation of the request body</returns>
    string CreateRequestFromText(
        string userInput,
        string? systemPrompt = null,
        int maxTokens = -1,
        double temperature = 0.2,
        string? modelId = null,
        ModelEffort? effort = null);

    /// <summary>
    /// Creates a request body for the model family from a content sequence.
    /// </summary>
    /// <param name="content">The content sequence (may include images, text, etc.)</param>
    /// <param name="systemPrompt">Optional system prompt</param>
    /// <param name="maxTokens">Maximum tokens to generate</param>
    /// <param name="temperature">Temperature for generation</param>
    /// <param name="modelId">The specific model ID to use</param>
    /// <param name="effort">The effort level for adaptive-generation models; ignored by models without effort support</param>
    /// <returns>JSON string representation of the request body</returns>
    string CreateRequestFromContentSequence(
        RequestContentSequence content,
        string? systemPrompt = null,
        int maxTokens = -1,
        double temperature = 0.2,
        string? modelId = null,
        ModelEffort? effort = null);

    /// <summary>
    /// Creates a request body for the model family from a content sequence.
    /// </summary>
    /// <param name="content">The content sequence (may include images, text, etc.)</param>
    /// <param name="systemPrompt">Optional system prompt</param>
    /// <param name="maxTokens">Maximum tokens to generate</param>
    /// <param name="temperature">Temperature for generation</param>
    /// <param name="modelId">The specific model ID to use</param>
    /// <param name="effort">The effort level for adaptive-generation models; ignored by models without effort support</param>
    /// <returns>JSON string representation of the request body</returns>
    string CreateRequestWithCachingFromContentSequence(
        RequestContentSequence content,
        RequestContentSequence cachedContent,
        string? systemPrompt = null,
        int maxTokens = -1,
        double temperature = 0.2,
        string? modelId = null,
        ModelEffort? effort = null);

    /// <summary>
    /// Parses the AWS Bedrock response for this model family.
    /// </summary>
    /// <param name="responseJson">The JSON response body from AWS Bedrock</param>
    /// <returns>Parsed response content</returns>
    ResponseContentSequence ParseResponse(string responseJson);

    /// <summary>
    /// Estimates the number of tokens for a structured message with proper handling of images.
    /// This method provides accurate token estimation for multimodal content by separately
    /// counting text tokens (via BPE) and image tokens (via provider-specific formulas).
    /// </summary>
    /// <param name="message">The message containing text and/or image content</param>
    /// <param name="systemPrompt">Optional system prompt that will be included in the request</param>
    /// <returns>Estimated token count including text, images, and overhead</returns>
    int EstimateTokensFromMessage(Anthropic.ApiModel.Message message, string? systemPrompt = null);

    /// <summary>
    /// Determines if the given model ID belongs to this model family.
    /// </summary>
    /// <param name="modelId">The model ID to check</param>
    /// <returns>True if the model belongs to this family</returns>
    bool SupportsModel(string modelId);

    /// <summary>
    /// Creates a request body for the model family with thinking mode enabled.
    /// Thinking mode allows the model to show its reasoning process before providing an answer.
    /// This is currently only supported by Anthropic Claude models.
    /// </summary>
    /// <param name="userInput">The user input text</param>
    /// <param name="systemPrompt">Optional system prompt</param>
    /// <param name="maxTokens">Maximum tokens to generate</param>
    /// <param name="thinkingBudget">Maximum tokens allocated for thinking</param>
    /// <param name="temperature">Temperature for generation (typically 1.0 for thinking mode)</param>
    /// <param name="modelId">The specific model ID to use</param>
    /// <param name="effort">The effort level for adaptive-generation models; ignored by models without effort support</param>
    /// <returns>JSON string representation of the request body</returns>
    /// <exception cref="NotSupportedException">Thrown if the model family doesn't support thinking mode</exception>
    string CreateThinkingRequestFromText(
        string userInput,
        string? systemPrompt = null,
        int maxTokens = -1,
        int thinkingBudget = -1,
        double temperature = 1.0,
        string? modelId = null,
        ModelEffort? effort = null)
    {
        throw new NotSupportedException($"Thinking mode is not supported by {ModelFamily} models");
    }

    /// <summary>
    /// Creates a request body for the model family with thinking mode enabled.
    /// Thinking mode allows the model to show its reasoning process before providing an answer.
    /// This is currently only supported by Anthropic Claude models.
    /// </summary>
    /// <param name="content">The content sequence (may include images, text, etc.)</param>
    /// <param name="systemPrompt">Optional system prompt</param>
    /// <param name="maxTokens">Maximum tokens to generate</param>
    /// <param name="thinkingBudget">Maximum tokens allocated for thinking</param>
    /// <param name="temperature">Temperature for generation (typically 1.0 for thinking mode)</param>
    /// <param name="modelId">The specific model ID to use</param>
    /// <param name="effort">The effort level for adaptive-generation models; ignored by models without effort support</param>
    /// <returns>JSON string representation of the request body</returns>
    /// <exception cref="NotSupportedException">Thrown if the model family doesn't support thinking mode</exception>
    string CreateThinkingRequestFromContentSequence(
        RequestContentSequence content,
        string? systemPrompt = null,
        int maxTokens = -1,
        int thinkingBudget = -1,
        double temperature = 1.0,
        string? modelId = null,
        ModelEffort? effort = null)
    {
        throw new NotSupportedException($"Thinking mode is not supported by {ModelFamily} models");
    }

    /// <summary>
    /// Creates a request body for the model family with thinking mode enabled and caching.
    /// Thinking mode allows the model to show its reasoning process before providing an answer.
    /// This is currently only supported by Anthropic Claude models.
    /// </summary>
    /// <param name="content"></param>
    /// <param name="cachedContent"></param>
    /// <param name="systemPrompt"></param>
    /// <param name="maxTokens"></param>
    /// <param name="thinkingBudget"></param>
    /// <param name="temperature"></param>
    /// <param name="modelId"></param>
    /// <returns></returns>
    /// <exception cref="NotSupportedException"></exception>
    string CreateThinkingRequestWithCachingFromContentSequence(
        RequestContentSequence content,
        RequestContentSequence cachedContent,
        string? systemPrompt = null,
        int maxTokens = -1,
        int thinkingBudget = -1,
        double temperature = 1.0,
        string? modelId = null,
        ModelEffort? effort = null)
    {
        throw new NotSupportedException($"Thinking mode is not supported by {ModelFamily} models");
    }
}

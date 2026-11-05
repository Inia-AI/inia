using IniaAi.AiConnection.AwsBedrock;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Common.Content;
using IniaAi.Common.Enums;

namespace IniaAi.AiConnection.Abstractions;

public interface IChatConnector
{
    const string ERROR_PREFIX = "ERROR: ";

    /// <summary>
    /// The largest image the model a call with these arguments would use reads without
    /// downscaling it. Render an image for that call to fit it. Throws like a call would when
    /// <paramref name="model"/> is a model the deployment cannot call.
    /// </summary>
    /// <param name="model">A specific model, such as a tenant's override; null for the tier's model.</param>
    VisionTier GetVisionTier(bool shouldUseAdvancedModel, string? model = null);

    /// <summary>
    /// The model id a call with these arguments sends, in the form this deployment calls it: a
    /// Bedrock inference-profile id, a Foundry deployment name or a public API id. Throws like a
    /// call would when <paramref name="model"/> is a model the deployment cannot call.
    /// </summary>
    /// <param name="model">A specific model, such as a tenant's override; null for the tier's model.</param>
    string GetModelId(bool shouldUseAdvancedModel, string? model = null);

    /// <summary>
    /// Gets the response from the AI model.
    /// </summary>
    /// <param name="userInput">The user input.</param>
    /// <param name="systemPrompt">The system prompt.</param>
    /// <param name="maxTokens">The maximum number of tokens.</param>
    /// <param name="shouldUseAdvancedModel">A flag indicating whether to use
    /// the more advanced model. If <see langword="false"/>, the simpler model
    /// will be used.</param>
    /// <param name="model">The specific model to use. If specified, overrides family and shouldUseAdvancedModel.</param>
    /// <param name="family">The model family to use. If null, uses provider default.</param>
    /// <param name="effort">The effort level for adaptive-generation models; ignored by models without effort support.</param>
    /// <returns>The response from the AI model.</returns>
    Task<ResponseContentSequence> GetResponseAsync(
        string userInput,
        string? systemPrompt = null,
        int maxTokens = -1,
        bool shouldUseAdvancedModel = true,
        string? model = null,
        ModelFamily? family = null,
        ModelEffort? effort = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the response from the AI model.
    /// </summary>
    /// <param name="content">The content sequence.</param>
    /// <param name="systemPrompt">The system prompt.</param>
    /// <param name="maxTokens">The maximum number of tokens.</param>
    /// <param name="shouldUseAdvancedModel">A flag indicating whether to use
    /// the more advanced model. If <see langword="false"/>, the simpler model
    /// will be used.</param>
    /// <param name="model">The specific model to use. If specified, overrides family and shouldUseAdvancedModel.</param>
    /// <param name="family">The model family to use. If null, uses provider default.</param>
    /// <param name="effort">The effort level for adaptive-generation models; ignored by models without effort support.</param>
    /// <returns>The response from the AI model.</returns>
    Task<ResponseContentSequence> GetResponseAsync(
        RequestContentSequence content,
        string? systemPrompt = null,
        int maxTokens = -1,
        bool shouldUseAdvancedModel = true,
        string? model = null,
        ModelFamily? family = null,
        ModelEffort? effort = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a response from the Anthropic API with caching. This is an
    /// experimental feature of Anthropic's API and it is not available for
    /// other <see cref="IChatConnector"/>s.
    ///
    /// <para>
    /// Minimum token limit for caching is 2048 tokens for simple models and
    /// 1024 tokens for advanced models.
    /// </para>
    ///
    /// <para>
    /// The <paramref name="cachedContent"/> should be the content that you want
    /// to cache. For the cache to be hit this content should be the same as the
    /// content that was sent to the API in the previous request.
    /// </para>
    ///
    /// <para>
    /// The cache has a 5-minute lifetime, refreshed each time the cached
    /// content is used.
    /// </para>
    ///
    /// <para>
    /// More: https://docs.anthropic.com/en/docs/build-with-claude/prompt-caching
    /// </para>
    /// </summary>
    /// <param name="content">Content to send to the API</param>
    /// <param name="cachedContent">Content to cache</param>
    /// <param name="systemPrompt">System prompt to guide the AI</param>
    /// <param name="maxTokens">Maximum tokens to output</param>
    /// <param name="shouldUseAdvancedModel">Whether to use the advanced model</param>
    /// <param name="model">The specific model to use. If specified, overrides family and shouldUseAdvancedModel.</param>
    /// <param name="family">The model family to use. If null, uses provider default.</param>
    /// <param name="effort">The effort level for adaptive-generation models; ignored by models without effort support.</param>
    /// <returns>The response from the API</returns>
    Task<ResponseContentSequence> GetResponseWithCachingAsync(
        RequestContentSequence content,
        RequestContentSequence cachedContent,
        string? systemPrompt = null,
        int maxTokens = -1,
        bool shouldUseAdvancedModel = true,
        string? model = null,
        ModelFamily? family = null,
        ModelEffort? effort = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a response using thinking mode (currently only supported by Anthropic models).
    /// Thinking mode allows the model to show its reasoning process before providing an answer.
    /// </summary>
    /// <param name="content">The content sequence.</param>
    /// <param name="systemPrompt">The system prompt.</param>
    /// <param name="maxTokens">The maximum number of tokens for the response.</param>
    /// <param name="thinkingBudget">The maximum number of tokens allocated for thinking.</param>
    /// <param name="shouldUseAdvancedModel">Whether to use the advanced model.</param>
    /// <param name="model">The specific model to use.</param>
    /// <param name="effort">The effort level for adaptive-generation models; ignored by models without effort support.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response from the AI model including thinking process.</returns>
    Task<ResponseContentSequence> GetThinkingResponseAsync(
        RequestContentSequence content,
        string? systemPrompt = null,
        int maxTokens = -1,
        int thinkingBudget = -1,
        bool shouldUseAdvancedModel = true,
        string? model = null,
        ModelEffort? effort = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"Thinking mode is not supported by {GetType().Name}");
    }

    /// <summary>
    /// Gets a response using thinking mode with caching (currently only supported by Anthropic models).
    /// Thinking mode allows the model to show its reasoning process before providing an answer.
    /// </summary>
    /// <param name="content">The content sequence.</param>
    /// <param name="cachedContent">The content to cache.</param>
    /// <param name="systemPrompt">The system prompt.</param>
    /// <param name="maxTokens">The maximum number of tokens for the response.</param>
    /// <param name="thinkingBudget">The maximum number of tokens allocated for thinking.</param>
    /// <param name="model">The specific model to use.</param>
    /// <param name="effort">The effort level for adaptive-generation models; ignored by models without effort support.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response from the AI model including thinking process.</returns>
    Task<ResponseContentSequence> GetThinkingResponseWithCachingAsync(
        RequestContentSequence content,
        RequestContentSequence cachedContent,
        string? systemPrompt = null,
        int maxTokens = -1,
        int thinkingBudget = -1,
        string? model = null,
        ModelEffort? effort = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"Thinking mode with caching is not supported by {GetType().Name}");
    }
}

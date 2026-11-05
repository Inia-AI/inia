using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.AwsBedrock;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Common.Content;
using IniaAi.Common.Enums;
using IniaAi.Testing.XunitAttributes;

namespace IniaAi.Testing.DependencyInjection.Mocks;

/// <summary>
/// What <see cref="TestChatConnectorFactory"/> hands out when <see cref="PaidApi"/> forbids paid
/// calls. A call that reaches it throws an error that names the opt-in, instead of spending money.
/// </summary>
public sealed class RefusingChatConnector : IChatConnector
{
    public VisionTier GetVisionTier(bool shouldUseAdvancedModel, string? model = null)
    {
        return VisionTier.HighResolution;
    }

    /// <summary>Names no model: no call reaches one.</summary>
    public string GetModelId(bool shouldUseAdvancedModel, string? model = null)
    {
        return model ?? string.Empty;
    }

    public Task<ResponseContentSequence> GetResponseAsync(
        string userInput,
        string? systemPrompt = null,
        int maxTokens = -1,
        bool shouldUseAdvancedModel = true,
        string? model = null,
        ModelFamily? family = null,
        ModelEffort? effort = null,
        CancellationToken cancellationToken = default)
    {
        throw Refusal();
    }

    public Task<ResponseContentSequence> GetResponseAsync(
        RequestContentSequence content,
        string? systemPrompt = null,
        int maxTokens = -1,
        bool shouldUseAdvancedModel = true,
        string? model = null,
        ModelFamily? family = null,
        ModelEffort? effort = null,
        CancellationToken cancellationToken = default)
    {
        throw Refusal();
    }

    public Task<ResponseContentSequence> GetResponseWithCachingAsync(
        RequestContentSequence content,
        RequestContentSequence cachedContent,
        string? systemPrompt = null,
        int maxTokens = -1,
        bool shouldUseAdvancedModel = true,
        string? model = null,
        ModelFamily? family = null,
        ModelEffort? effort = null,
        CancellationToken cancellationToken = default)
    {
        throw Refusal();
    }

    public Task<ResponseContentSequence> GetThinkingResponseAsync(
        RequestContentSequence content,
        string? systemPrompt = null,
        int maxTokens = -1,
        int thinkingBudget = -1,
        bool shouldUseAdvancedModel = true,
        string? model = null,
        ModelEffort? effort = null,
        CancellationToken cancellationToken = default)
    {
        throw Refusal();
    }

    public Task<ResponseContentSequence> GetThinkingResponseWithCachingAsync(
        RequestContentSequence content,
        RequestContentSequence cachedContent,
        string? systemPrompt = null,
        int maxTokens = -1,
        int thinkingBudget = -1,
        string? model = null,
        ModelEffort? effort = null,
        CancellationToken cancellationToken = default)
    {
        throw Refusal();
    }

    private static InvalidOperationException Refusal()
    {
        return new InvalidOperationException(
            $"A test called the real model, and local test runs make no paid calls. Give the test a {nameof(ScriptedChatConnector)}, " +
            $"or mark it [PaidApiFact] and run it with {PaidApi.OPT_IN_VARIABLE}=1.");
    }
}

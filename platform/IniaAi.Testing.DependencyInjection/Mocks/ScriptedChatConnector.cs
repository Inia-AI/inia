using System.Collections.Concurrent;
using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.AwsBedrock;
using IniaAi.AiConnection.Common;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.AiConnection.Common.ChatConnector.Enums;
using IniaAi.Common.Content;
using IniaAi.Common.Enums;

namespace IniaAi.Testing.DependencyInjection.Mocks;

/// <summary>
/// Stands in for the model in tests of the code around a model call, so they need no API key and
/// spend no tokens. Each call is answered from the request it carries, and every call is kept so a
/// test can check what the code sent. A test whose assertion is the model's own judgment keeps the
/// real connector from <see cref="TestChatConnectorFactory"/>. A call that
/// <paramref name="refuse"/> picks out is refused the way the provider refuses it: the answer is
/// replaced by the refusal and the tokens are still counted. A test that sets
/// <paramref name="endpointModelOf"/> gets each response stamped with the id the call was sent
/// to, as the Bedrock provider stamps it, from the model the call named.
/// </summary>
public sealed class ScriptedChatConnector(
    Func<ScriptedModelCall, string> answer,
    Func<ScriptedModelCall, ModelRefusal?>? refuse = null,
    Func<string?, string>? endpointModelOf = null) : IChatConnector
{
    public const string MODEL_ID = "claude-haiku-4-5-20251001";

    private const int CHARACTERS_PER_TOKEN = 4;

    private readonly ConcurrentQueue<ScriptedModelCall> _calls = new();

    public IReadOnlyCollection<ScriptedModelCall> Calls => _calls;

    public static ScriptedChatConnector Answering(string text)
    {
        return new ScriptedChatConnector(_ => text);
    }

    public VisionTier GetVisionTier(bool shouldUseAdvancedModel, string? model = null)
    {
        return VisionTier.Standard;
    }

    public string GetModelId(bool shouldUseAdvancedModel, string? model = null)
    {
        return model ?? MODEL_ID;
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
        return Task.FromResult(Respond(new RequestContentSequence { userInput }, systemPrompt, model, cancellationToken));
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
        return Task.FromResult(Respond(content, systemPrompt, model, cancellationToken));
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
        return Task.FromResult(Respond([.. cachedContent, .. content], systemPrompt, model, cancellationToken));
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
        return Task.FromResult(Respond(content, systemPrompt, model, cancellationToken));
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
        return Task.FromResult(Respond([.. cachedContent, .. content], systemPrompt, model, cancellationToken));
    }

    private ResponseContentSequence Respond(RequestContentSequence content, string? systemPrompt, string? model, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var call = new ScriptedModelCall(
            content.GetPlainText() ?? string.Empty,
            content.Any(part => part.Type == CommonContentType.Image),
            systemPrompt);
        _calls.Enqueue(call);
        string text = answer(call);

        var response = new ResponseContentSequence
        {
            InputTokens = Math.Max(1, call.Text.Length / CHARACTERS_PER_TOKEN),
            OutputTokens = Math.Max(1, text.Length / CHARACTERS_PER_TOKEN),
        };
        response.Add(new ResponseCommonContent(text));
        response.Metadata[ModelIdNormalizer.MODEL_METADATA_KEY] = MODEL_ID;

        if (endpointModelOf is not null)
        {
            response.Metadata[ModelIdNormalizer.ENDPOINT_MODEL_METADATA_KEY] = endpointModelOf(model);
        }

        if (refuse?.Invoke(call) is ModelRefusal refusal)
        {
            response.MarkRefused(refusal);
        }

        return response;
    }
}

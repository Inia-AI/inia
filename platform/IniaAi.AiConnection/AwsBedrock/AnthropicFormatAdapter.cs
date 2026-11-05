using IniaAi.AiConnection.Anthropic.ApiModel;
using IniaAi.AiConnection.Anthropic.ApiModel.Bedrock;
using IniaAi.AiConnection.Anthropic.ApiModel.Enums;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Common;
using IniaAi.Common.Enums;
using IniaAi.Common.Extensions;
using IniaAi.Configuration;
using IniaAi.Configuration.Enums.Anthropic;
using Microsoft.Extensions.Logging;
using SharpToken;
using Message = IniaAi.AiConnection.Anthropic.ApiModel.Message;
using ResponseBody = IniaAi.AiConnection.Anthropic.ApiModel.ResponseBody;

namespace IniaAi.AiConnection.AwsBedrock;

/// <summary>
/// Format adapter for Anthropic Claude models on AWS Bedrock.
/// </summary>
internal class AnthropicFormatAdapter : IModelFormatAdapter
{
    private readonly PlatformSettings _platformSettings;
    private readonly ILogger<AnthropicFormatAdapter> _logger;
    private readonly GptEncoding _encoding;

    public ModelFamily ModelFamily => ModelFamily.Anthropic;

    public AnthropicFormatAdapter(
        PlatformSettings platformSettings,
        ILogger<AnthropicFormatAdapter> logger)
    {
        _platformSettings = platformSettings;
        _logger = logger;
        _encoding = GptEncoding.GetEncoding("cl100k_base");
    }

    public string CreateRequestFromText(
        string userInput,
        string? systemPrompt = null,
        int maxTokens = -1,
        double temperature = 0.2,
        string? modelId = null,
        ModelEffort? effort = null)
    {
        var message = new Message(new ContentRequest(new(userInput)));
        return CreateRequestBody(
            message,
            systemPrompt,
            maxTokens,
            temperature,
            modelId,
            effort);
    }

    public string CreateRequestFromContentSequence(
        RequestContentSequence content,
        string? systemPrompt = null,
        int maxTokens = -1,
        double temperature = 0.2,
        string? modelId = null,
        ModelEffort? effort = null)
    {
        var message = new Message(content);
        return CreateRequestBody(
            message,
            systemPrompt,
            maxTokens,
            temperature,
            modelId,
            effort);
    }

    public string CreateRequestWithCachingFromContentSequence(
        RequestContentSequence content,
        RequestContentSequence cachedContent,
        string? systemPrompt = null,
        int maxTokens = -1,
        double temperature = 0.2,
        string? modelId = null,
        ModelEffort? effort = null)
    {
        var message = new Message(ContentRequest.CreateCachedContent(cachedContent));
        message.Add(content);

        return CreateRequestBody(
            message,
            systemPrompt,
            maxTokens,
            temperature,
            modelId,
            effort);
    }

    public string CreateThinkingRequestFromText(
        string userInput,
        string? systemPrompt = null,
        int maxTokens = -1,
        int thinkingBudget = -1,
        double temperature = 1.0,
        string? modelId = null,
        ModelEffort? effort = null)
    {
        var message = new Message(new ContentRequest(new(userInput)));
        return CreateThinkingRequestBody(
            message,
            systemPrompt,
            maxTokens,
            thinkingBudget,
            temperature,
            modelId,
            effort);
    }

    public string CreateThinkingRequestFromContentSequence(
        RequestContentSequence content,
        string? systemPrompt = null,
        int maxTokens = -1,
        int thinkingBudget = -1,
        double temperature = 1.0,
        string? modelId = null,
        ModelEffort? effort = null)
    {
        var message = new Message(content);
        return CreateThinkingRequestBody(
            message,
            systemPrompt,
            maxTokens,
            thinkingBudget,
            temperature,
            modelId,
            effort);
    }

    public string CreateThinkingRequestWithCachingFromContentSequence(
        RequestContentSequence content,
        RequestContentSequence cachedContent,
        string? systemPrompt = null,
        int maxTokens = -1,
        int thinkingBudget = -1,
        double temperature = 1.0,
        string? modelId = null,
        ModelEffort? effort = null)
    {
        var message = new Message(ContentRequest.CreateCachedContent(cachedContent));
        message.Add(content);

        return CreateThinkingRequestBody(
            message,
            systemPrompt,
            maxTokens,
            thinkingBudget,
            temperature,
            modelId,
            effort);
    }

    public ResponseContentSequence ParseResponse(string responseJson)
    {
        if (string.IsNullOrWhiteSpace(responseJson))
        {
            _logger.LogError("Received empty or null response JSON for Anthropic model");
            throw new InvalidOperationException("Received empty or null response JSON for Anthropic model");
        }

        ResponseBody anthropicResponse = ResponseBody.FromJson(responseJson)
            ?? throw new InvalidOperationException("Failed to parse Anthropic response");

        return anthropicResponse.FormatResponseContent();
    }

    public int EstimateTokensFromMessage(Message message, string? systemPrompt = null)
    {
        int textTokens = 0;
        int imageTokens = 0;
        int imageCount = 0;

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            textTokens += _encoding.Encode(systemPrompt).Count;
        }

        foreach (ContentRequest content in message.Content)
        {
            if (content.Type is ContentType.Text
                && !string.IsNullOrWhiteSpace(content.Text))
            {
                textTokens += _encoding.Encode(content.Text).Count;
            }
            else if (content.Type is ContentType.Image
                && content.Source is not null)
            {
                // Anthropic's image token calculation:
                // Standard resolution images are approximately 1,568 tokens each
                // This is based on Anthropic's documented token counting methodology
                // High-resolution images use a tile-based approach, but for estimation
                // we use the standard base rate as a conservative estimate
                const int tokensPerImage = 1568;
                imageTokens += tokensPerImage;
                imageCount++;
            }
        }

        const int estimatedRoleOverhead = 5;
        const int estimatedBaseOverhead = 10;
        const int estimatedFudgeOverhead = 500;

        int totalTokens = textTokens + imageTokens + estimatedFudgeOverhead + estimatedRoleOverhead + estimatedBaseOverhead;
        int final = (int)Math.Ceiling(totalTokens * 1.1); // 10% safety margin

        _logger.LogInformation(
            "TokenEstimate: Text={TextTokens} tokens, Images={ImageCount}x1568={ImageTokens} tokens, " +
            "Overhead={Overhead}, Total={TotalBeforeMargin}, Final={Final}",
            textTokens,
            imageCount,
            imageTokens,
            estimatedFudgeOverhead + estimatedRoleOverhead + estimatedBaseOverhead,
            totalTokens,
            final);

        return final;
    }

    public bool SupportsModel(string modelId)
    {
        return !string.IsNullOrWhiteSpace(modelId) && Utils.TryParseEnumFromEnumMemberAttribute(modelId, out AiModel _);
    }

    private string CreateRequestBody(
        Message message,
        string? systemPrompt,
        int maxTokens,
        double temperature,
        string? modelId,
        ModelEffort? effort = null)
    {
        AiModel? model = ModelRequestShaper.ParseModelId(modelId);
        maxTokens = DetermineMaxTokensToUse(
            maxTokens,
            model,
            shouldUseThinkingMode: false);

        LogMessageContent(message);

        var requestBody = new BedrockRequestBody(message, maxTokens, systemPrompt)
        {
            AnthropicVersion = "bedrock-2023-05-31"
        };

        if (!ModelRequestShaper.TryApplyAdaptiveGenerationShape(requestBody, model, shouldUseThinkingMode: false, effort))
        {
            requestBody.Temperature = temperature;
        }

        string json = requestBody.ToJson(pretty: false);

        _logger.LogDebug(
            "Anthropic Bedrock request body: {Json}",
            json);

        return json;
    }

    private string CreateThinkingRequestBody(
        Message message,
        string? systemPrompt,
        int maxTokens,
        int thinkingBudget,
        double temperature,
        string? modelId,
        ModelEffort? effort = null)
    {
        AiModel? model = ModelRequestShaper.ParseModelId(modelId);
        maxTokens = DetermineMaxTokensToUse(
            maxTokens,
            model,
            shouldUseThinkingMode: true);

        LogMessageContent(message);

        var requestBody = new BedrockRequestBody(message, maxTokens, systemPrompt)
        {
            AnthropicVersion = "bedrock-2023-05-31"
        };

        if (!ModelRequestShaper.TryApplyAdaptiveGenerationShape(requestBody, model, shouldUseThinkingMode: true, effort))
        {
            ValidateThinkingBudget(maxTokens, thinkingBudget);
            requestBody.Temperature = temperature;
            requestBody.ThinkingSettings = new ThinkingSettings(thinkingBudget);
        }

        string json = requestBody.ToJson(pretty: false);

        _logger.LogDebug(
            "Anthropic Bedrock thinking mode request body: {Json}",
            json);

        return json;
    }

    private int DetermineMaxTokensToUse(int maxTokens, AiModel? model, bool shouldUseThinkingMode)
    {
        int defaultTokenCap = maxTokens > 0
            ? Math.Min(_platformSettings.Anthropic.AdvancedModelMaxTokens, maxTokens)
            : _platformSettings.Anthropic.AdvancedModelMaxTokens;

        if (model is null)
        {
            return defaultTokenCap;
        }

        int tokenCap = shouldUseThinkingMode && model.Value.IsThinkingModel()
            ? 120000
            : model.Value.GetMaxTokens()
                ?? defaultTokenCap;

        return Math.Min(tokenCap, maxTokens > 0 ? maxTokens : tokenCap);
    }

    private static void ValidateThinkingBudget(int maxTokens, int thinkingBudget)
    {
        if (thinkingBudget < 1024)
        {
            throw new ArgumentException(
                "Thinking budget must be at least 1024 tokens",
                nameof(thinkingBudget));
        }

        if (thinkingBudget > maxTokens)
        {
            throw new ArgumentException(
                $"Thinking budget cannot be greater than the maximum token limit, budget: {thinkingBudget}, max tokens: {maxTokens}",
                nameof(thinkingBudget));
        }
    }

    private void LogMessageContent(Message message)
    {
        int textCount = message.Content.Count(c => c.Type is ContentType.Text);
        int imageCount = message.Content.Count(c => c.Type is ContentType.Image);

        _logger.LogInformation(
            "Message content: {Role} role, {TextCount} text content, {ImageCount} image content",
            message.Role,
            textCount,
            imageCount);
    }
}

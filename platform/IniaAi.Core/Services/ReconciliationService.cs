using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.AwsBedrock;
using IniaAi.AiConnection.Common;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Common;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Configuration.Platform;
using IniaAi.Domain.Tools.Reconciliation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IniaAi.Core.Services;

public class ReconciliationService : ITokenAccumulator
{
    private readonly IniaAiSettings _settings;
    private readonly ILogger<ReconciliationService> _logger;
    private readonly IChatConnector _chatConnector;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private ReconciliationPromptsLoader _promptsLoader;

    // Token accumulation for tracking (thread-safe for parallel calls)
    private int _accumulatedInputTokens;
    private int _accumulatedOutputTokens;

    #region ITokenAccumulator Implementation

    public int AccumulatedInputTokens => _accumulatedInputTokens;
    public int AccumulatedOutputTokens => _accumulatedOutputTokens;
    public int AccumulatedCacheCreationTokens => 0;
    public int AccumulatedCacheReadTokens => 0;
    public string? LastUsedModelId { get; private set; }

    public void ResetTokenAccumulator()
    {
        _ = Interlocked.Exchange(ref _accumulatedInputTokens, 0);
        _ = Interlocked.Exchange(ref _accumulatedOutputTokens, 0);
        LastUsedModelId = null;
    }

    private void AccumulateTokens(ResponseContentSequence response)
    {
        _ = Interlocked.Add(ref _accumulatedInputTokens, response.InputTokens);
        _ = Interlocked.Add(ref _accumulatedOutputTokens, response.OutputTokens);

        // Capture model ID from response metadata
        if (response.Metadata.TryGetValue(ModelIdNormalizer.MODEL_METADATA_KEY, out object? modelValue)
            && modelValue is string modelString
            && !string.IsNullOrWhiteSpace(modelString))
        {
            LastUsedModelId = modelString;
        }
    }

    #endregion

    public ReconciliationService(
        IniaAiSettings settings,
        ILogger<ReconciliationService> logger,
        IChatConnector chatConnector,
        IServiceScopeFactory serviceScopeFactory)
    {
        _settings = settings;
        _logger = logger;
        _chatConnector = chatConnector;
        _serviceScopeFactory = serviceScopeFactory;
        _promptsLoader = new ReconciliationPromptsLoader(logger, serviceScopeFactory);

        _ = _settings;
    }

    public ReconciliationService(
        IOptions<IniaAiSettings> options,
        ILogger<ReconciliationService> logger,
        IChatConnector chatConnector,
        IServiceScopeFactory serviceScopeFactory) : this(
            options.Value,
            logger,
            chatConnector,
            serviceScopeFactory)
    {
    }

    /// <summary>
    /// The newest stored prompt row, the one Tools Settings edits, read once until
    /// <see cref="ReloadPrompts"/>; the code defaults when no row is stored or the read fails.
    /// </summary>
    public Task<ReconciliationPrompts> GetPromptsAsync()
    {
        return _promptsLoader.LoadAsync();
    }

    /// <summary>
    /// Makes the next <see cref="GetPromptsAsync"/> read the newest stored row, so a run that starts
    /// after a Tools Settings edit asks the edited prompts.
    /// </summary>
    public void ReloadPrompts()
    {
        _promptsLoader = new ReconciliationPromptsLoader(_logger, _serviceScopeFactory);
    }

    public async Task<string?> FindMatchFromListAsync(
        string identifiedName,
        List<string> potentialNames,
        ModelFamily family,
        string? promptTemplate = null)
    {
        ReconciliationPrompts prompts = await GetPromptsAsync().ConfigureAwait(false);
        string userPrompt = string.Format(
            promptTemplate ?? prompts.FindMatchPrompt,
            identifiedName,
            string.Join("\n", potentialNames).Trim());

        _logger.LogTrace("User prompt for matching: {UserPrompt}", userPrompt);

        ResponseContentSequence responseContent = await _chatConnector.GetResponseAsync(
            userPrompt,
            prompts.SystemPrompt,
            shouldUseAdvancedModel: true,
            family: family);

        AccumulateTokens(responseContent);

        string response = responseContent.GetPlainText() ?? string.Empty;

        _logger.LogInformation(
            "Matching response from the AI model: {Response}",
            response);

        string? answer =
            Utils.GetStringFromTag(response, "answer");

        return answer;
    }

    public async Task<bool> CompareListsAsync(
        IEnumerable<string> listA,
        IEnumerable<string> listB,
        ModelFamily family,
        string? promptTemplate = null)
    {
        ReconciliationPrompts prompts = await GetPromptsAsync().ConfigureAwait(false);
        string userPrompt = string.Format(
            promptTemplate ?? prompts.CompareListsPrompt,
            string.Join("\n", listA).Trim(),
            string.Join("\n", listB).Trim());

        _logger.LogTrace("User prompt for comparing lists: {UserPrompt}", userPrompt);

        ResponseContentSequence responseContent = await _chatConnector.GetResponseAsync(
            userPrompt,
            prompts.SystemPrompt,
            shouldUseAdvancedModel: true,
            family: family);

        AccumulateTokens(responseContent);

        string response = responseContent.GetPlainText() ?? string.Empty;

        _logger.LogInformation(
            "Comparing lists response from the AI model: {Response}",
            response);

        string? answer = Utils.GetStringFromTag(response, "answer");

        return answer?.Trim().ToLower() is "true" or "yes";
    }
}

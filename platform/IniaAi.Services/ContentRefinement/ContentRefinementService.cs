using System.Text;
using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.AwsBedrock;
using IniaAi.AiConnection.Common;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Application.Commands.TextProcessing.RefineContent;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Common;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Common.Text;
using Microsoft.Extensions.Logging;

namespace IniaAi.Services.ContentRefinement;

public class ContentRefinementService : IContentRefinementService, ITokenAccumulator
{
    private const string SystemPrompt =
        """
        You are a content refinement assistant. You rewrite text on behalf of the user.
        """;

    private const string TaskHeader =
        """
        ## TASK

        Refine <current_text> according to <request>.

        ---

        ## INPUTS
        """;

    private const string InstructionsAndOutputFormat =
        """
        ---

        ## INSTRUCTIONS

        1. Classify <request> as IN SCOPE or OUT OF SCOPE:
           - IN SCOPE: rewriting <current_text> using only material that is already in <current_text> or supplied in <request>. Includes restructuring, tone/style/length/formality changes, grammar, formatting, summarising, incorporating any facts the user supplies in <request>, AND adding analysis, commentary, or opinions derived from material already in <current_text>. Examples: "make this more formal", "add this rating: Moody's A2" (supplied), "use this information: <...>", "add a brief opinion on these results".
           - OUT OF SCOPE: anything that is NOT a rewrite of <current_text> using available material. This is a catch-all and includes, non-exhaustively:
             - producing factual content (figures, citations, ratings, prices, dates, quotes, named entities, etc.) that is neither in <current_text> NOR in <request>. Examples: "add Moody's and S&P ratings" (none supplied), "cite a study on this", "insert Q3 revenue".
             - requests requiring external lookup or real-world knowledge not provided in <current_text> or <request>.
             - chit-chat or requests unrelated to <current_text>. Examples: "what's the weather", "tell me a joke", "send this to my manager".
        2. Consult <history> for context on how <current_text> evolved across prior turns. Do not re-emit an earlier <version> verbatim unless <request> explicitly asks to revert.
        3. Follow <request> precisely, applying it to <current_text>.

        ---

        ## RULES

        - Preserve the meaning, structure, and factual accuracy of <current_text>.
        - Do not remove content unless <request> explicitly asks.
        - Maintain the original language of <current_text>.
        - Use <br> tags or paragraph breaks for line spacing.
        - Placeholders like {{#FACT_1}}, {{#TABLE_2}}, {{#A_3}} are opaque tokens for preserved elements (citations, tables, links). Keep them in place. Never mention them in the explanation. They are NOT user-supplied facts.

        ---

        ## OUTPUT FORMAT

        Pick exactly ONE of the two forms below based on scope. Output the tags only — no prose before, between, or after them.

        ### Form A — when the request is IN SCOPE
        Emit BOTH of these tags, in this order, and nothing else:
        <explanation>1-2 sentence plain-language summary of the changes, no placeholder references.</explanation>
        <refined_content>refined text here</refined_content>

        ### Form B — when the request is OUT OF SCOPE
        Emit ONLY this tag, and nothing else (no <explanation>, no <refined_content>):
        <out_of_scope>true</out_of_scope>
        """;

    private const string OUT_OF_SCOPE_TAG = "out_of_scope";

    private readonly ILogger<ContentRefinementService> _logger;
    private readonly IChatConnector _chatConnector;
    private readonly TenantModelFamilyResolver _tenantModelFamilyResolver;
    private readonly IRequestModelUse _modelUse;

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

        if (response.Metadata.TryGetValue(ModelIdNormalizer.MODEL_METADATA_KEY, out object? modelValue)
            && modelValue is string modelString
            && !string.IsNullOrWhiteSpace(modelString))
        {
            LastUsedModelId = modelString;
        }
    }

    #endregion

    public ContentRefinementService(
        ILogger<ContentRefinementService> logger,
        IChatConnector chatConnector,
        TenantModelFamilyResolver tenantModelFamilyResolver,
        IRequestModelUse modelUse)
    {
        _logger = logger;
        _chatConnector = chatConnector;
        _tenantModelFamilyResolver = tenantModelFamilyResolver;
        _modelUse = modelUse;
    }

    public async Task<ContentRefinementOutput?> RefineAsync(
        string content,
        string instruction,
        IReadOnlyList<RefinementTurnDto> conversationHistory,
        string? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            _logger.LogWarning("Content refinement attempted with empty content");
            throw new ArgumentException("Content is required", nameof(content));
        }

        if (string.IsNullOrWhiteSpace(instruction))
        {
            _logger.LogWarning("Content refinement attempted with empty instruction");
            throw new ArgumentException("Instruction is required", nameof(instruction));
        }

        try
        {
            _logger.LogInformation(
                "Starting content refinement. Content length: {ContentLength}, History turns: {HistoryCount}",
                content.Length,
                conversationHistory.Count);

            string prompt = BuildPrompt(content, instruction, conversationHistory, _logger);

            ModelFamily family = _tenantModelFamilyResolver.GetModelFamilyForFunction(
                organizationId, FunctionType.Proofreading);

            _modelUse.MarkModelCalled();
            ResponseContentSequence responseContent = await _chatConnector.GetResponseAsync(
                prompt,
                systemPrompt: SystemPrompt,
                shouldUseAdvancedModel: true,
                model: _tenantModelFamilyResolver.GetModelOverride(organizationId, true),
                family: family,
                cancellationToken: cancellationToken);

            AccumulateTokens(responseContent);

            string response = responseContent.GetPlainText() ?? string.Empty;

            string? outOfScopeSignal = Utils.GetStringFromTag(response, OUT_OF_SCOPE_TAG);
            string? refinedContent = Utils.GetStringFromTag(response, "refined_content");

            bool isOutOfScope = outOfScopeSignal is not null
                && outOfScopeSignal.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

            if (isOutOfScope)
            {
                if (!string.IsNullOrWhiteSpace(refinedContent))
                {
                    _logger.LogWarning("Refinement response contained both <out_of_scope>true</out_of_scope> and <refined_content>; treating as out-of-scope");
                }

                _logger.LogInformation("Content refinement signalled out-of-scope");
                return ContentRefinementOutput.OutOfScope();
            }

            if (string.IsNullOrWhiteSpace(refinedContent))
            {
                _logger.LogWarning(
                    "Content refinement service returned empty result. Response: {Length} characters",
                    response.Length);
                return null;
            }

            string? explanation = Utils.GetStringFromTag(response, "explanation");

            _logger.LogInformation("Completed content refinement");

            return ContentRefinementOutput.Refined(refinedContent, explanation);
        }
        catch (Exception ex) when (ex is not ArgumentException and not OperationCanceledException)
        {
            _logger.LogError(ex, "Error during content refinement: {ErrorMessage}", ex.Message);
            throw;
        }
    }

    private static string BuildPrompt(
        string content,
        string instruction,
        IReadOnlyList<RefinementTurnDto> conversationHistory,
        ILogger logger)
    {
        var sb = new StringBuilder();

        _ = sb.AppendLine(TaskHeader);
        _ = sb.AppendLine();

        _ = sb.AppendLine("<current_text>");
        _ = sb.AppendLine(content);
        _ = sb.AppendLine("</current_text>");
        _ = sb.AppendLine();

        // Pair history entries into (user instruction, assistant refined version). The
        // frontend pushes the full restored refined text as the assistant turn content, so
        // each <version> here is the actual output of that turn. The most recent <version>
        // duplicates <current_text> above; this is intentional for clarity to the model.
        var pairs = new List<(string Instruction, string Version)>();
        for (int i = 0; i + 1 < conversationHistory.Count; i += 2)
        {
            RefinementTurnDto user = conversationHistory[i];
            RefinementTurnDto assistant = conversationHistory[i + 1];
            if (string.Equals(user.Role, "user", StringComparison.OrdinalIgnoreCase)
                && string.Equals(assistant.Role, "assistant", StringComparison.OrdinalIgnoreCase))
            {
                pairs.Add((user.Content, assistant.Content));
            }
            else
            {
                // Validator should have rejected this; if we get here a future caller bypassed it.
                logger.LogWarning(
                    "Skipping malformed conversation pair at index {Index}: roles=[{UserRole}, {AssistantRole}]",
                    i, LogSanitizer.ForLog(user.Role), LogSanitizer.ForLog(assistant.Role));
            }
        }

        if (conversationHistory.Count % 2 != 0)
        {
            logger.LogWarning(
                "Conversation history has odd length ({Count}); trailing turn will be ignored",
                conversationHistory.Count);
        }

        _ = sb.AppendLine("<history>");
        if (pairs.Count == 0)
        {
            _ = sb.AppendLine("This is the first refinement turn; no prior turns.");
        }
        else
        {
            for (int i = 0; i < pairs.Count; i++)
            {
                (string instructionText, string versionText) = pairs[i];
                _ = sb.AppendLine($"<turn index=\"{i + 1}\">");
                _ = sb.AppendLine("<instruction>");
                _ = sb.AppendLine(instructionText);
                _ = sb.AppendLine("</instruction>");
                _ = sb.AppendLine("<version>");
                _ = sb.AppendLine(versionText);
                _ = sb.AppendLine("</version>");
                _ = sb.AppendLine("</turn>");
            }
        }

        _ = sb.AppendLine("</history>");
        _ = sb.AppendLine();

        _ = sb.AppendLine("<request>");
        _ = sb.AppendLine(instruction);
        _ = sb.AppendLine("</request>");
        _ = sb.AppendLine();

        _ = sb.AppendLine(InstructionsAndOutputFormat);

        return sb.ToString();
    }
}

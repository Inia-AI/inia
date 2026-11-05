using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Common.Enums;
using IniaAi.Common.Extensions;
using IniaAi.Configuration.Enums.Anthropic;
using Microsoft.Extensions.Logging;

namespace IniaAi.AiConnection.Anthropic.ApiModel;

/// <summary>
/// An adaptive-thinking model takes no thinking budget, so at its default effort it can spend
/// the whole <c>max_tokens</c> on thinking and stop before it writes an answer. Anthropic's
/// remedy for an over-thought request is a lower effort: the request goes out once more at
/// <see cref="ModelEffort.Low"/>, which leaves the output for the answer.
/// </summary>
internal static class ThinkingExhaustion
{
    private static readonly string[] _cacheTokenKeys =
    [
        ResponseBody.CACHE_CREATION_INPUT_TOKENS_KEY,
        ResponseBody.CACHE_READ_INPUT_TOKENS_KEY,
    ];

    /// <summary>Sends at <paramref name="effort"/>, and once more at low effort when thinking took the whole output.</summary>
    public static async Task<ResponseContentSequence> SendAsync(
        Func<ModelEffort?, Task<ResponseContentSequence>> sendAsync,
        AiModel? model,
        ModelEffort? effort,
        ILogger logger)
    {
        ResponseContentSequence response = await sendAsync(effort);

        if (!response.IsThinkingOnly
            || !response.IsTruncated
            || model?.IsAdaptiveThinkingModel() is not true)
        {
            return response;
        }

        ModelEffort effortSent = effort ?? model.Value.GetDefaultEffort();

        if (effortSent is ModelEffort.Low)
        {
            return response;
        }

        logger.LogWarning(
            "{Model} spent all {OutputTokens} output tokens on thinking at {Effort} effort and wrote no answer; sending the request again at low effort",
            model,
            response.OutputTokens,
            effortSent);

        ResponseContentSequence retried = await sendAsync(ModelEffort.Low);
        AddUsage(retried, response);

        return retried;
    }

    private static void AddUsage(ResponseContentSequence target, ResponseContentSequence spent)
    {
        target.InputTokens += spent.InputTokens;
        target.OutputTokens += spent.OutputTokens;
        target.RequestCount += spent.RequestCount;

        foreach (string key in _cacheTokenKeys)
        {
            if (spent.Metadata.TryGetValue(key, out object? spentTokens))
            {
                target.Metadata[key] = Convert.ToInt32(spentTokens)
                    + (target.Metadata.TryGetValue(key, out object? targetTokens) ? Convert.ToInt32(targetTokens) : 0);
            }
        }
    }
}

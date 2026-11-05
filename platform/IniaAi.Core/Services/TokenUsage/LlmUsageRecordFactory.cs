using IniaAi.AiConnection.Common;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using TokenExecutionContext = IniaAi.Domain.Entities.TokenUsage.Enums.ExecutionContext;

namespace IniaAi.Core.Services.TokenUsage;

/// <summary>
/// Builds the usage record for what an <see cref="ITokenAccumulator"/> spent since its last
/// reset: the accumulated components under the model it last used, or nothing when it made
/// no call.
/// </summary>
public static class LlmUsageRecordFactory
{
    public static LlmUsageRecord? FromAccumulator(
        ITokenAccumulator accumulator,
        string fallbackModelId,
        string organizationId,
        string? userId,
        UsageType usageType,
        TokenExecutionContext executionContext,
        string description)
    {
        if (accumulator.AccumulatedInputTokens == 0 && accumulator.AccumulatedOutputTokens == 0)
        {
            return null;
        }

        string modelId = accumulator.LastUsedModelId ?? fallbackModelId;

        return new LlmUsageRecord
        {
            OrganizationId = organizationId,
            UserId = userId,
            UsageType = usageType,
            ExecutionContext = executionContext,
            Provider = ModelIdNormalizer.GetProvider(modelId),
            Family = ModelIdNormalizer.GetFamily(modelId),
            ModelId = ModelIdNormalizer.Normalize(modelId),
            Components = UsageComponentHelper.FromTokens(
                accumulator.AccumulatedInputTokens,
                accumulator.AccumulatedOutputTokens,
                accumulator.AccumulatedCacheCreationTokens,
                accumulator.AccumulatedCacheReadTokens),
            Description = description,
            EndpointModelId = accumulator.LastUsedModelId,
        };
    }
}

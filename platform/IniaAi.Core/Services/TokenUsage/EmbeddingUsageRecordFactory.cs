using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Common;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using TokenExecutionContext = IniaAi.Domain.Entities.TokenUsage.Enums.ExecutionContext;

namespace IniaAi.Core.Services.TokenUsage;

/// <summary>
/// Builds the usage record of embedding calls: the provider of the route the connector called and
/// the model name the price list uses, as a model call's record carries them. A caller adds the
/// fields of its own context with <c>with</c>.
/// </summary>
public static class EmbeddingUsageRecordFactory
{
    public static EmbeddingUsageRecord For(
        IEmbeddingsConnector connector,
        int tokens,
        string organizationId,
        UsageType usageType,
        TokenExecutionContext executionContext,
        string description)
    {
        return new EmbeddingUsageRecord
        {
            OrganizationId = organizationId,
            UsageType = usageType,
            ExecutionContext = executionContext,
            Provider = connector.Provider,
            Family = ModelIdNormalizer.GetFamily(connector.ModelId),
            ModelId = ModelIdNormalizer.Normalize(connector.ModelId),
            Components = UsageComponentHelper.ForEmbedding(tokens),
            Description = description,
        };
    }
}

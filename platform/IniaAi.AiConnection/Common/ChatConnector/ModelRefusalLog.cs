using Microsoft.Extensions.Logging;

namespace IniaAi.AiConnection.Common.ChatConnector;

/// <summary>
/// Writes the one log line per refused model request that the refusal alarm counts. The line
/// carries what the connector knows: the category, the model and the provider's ids. The block
/// or page that made the request logs its own line with the run's context.
/// </summary>
public static class ModelRefusalLog
{
    public static void Write(ILogger logger, ModelRefusal refusal, string modelId, string? providerRequestId)
    {
        logger.LogWarning(
            ModelRefusal.LOG_MARKER + ": the model declined the request. Category: {RefusalCategory}, model: {ModelId}, message: {MessageId}, provider request: {ProviderRequestId}, explanation: {RefusalExplanation}",
            refusal.Category,
            modelId,
            refusal.RequestId,
            providerRequestId,
            refusal.Explanation);
    }
}

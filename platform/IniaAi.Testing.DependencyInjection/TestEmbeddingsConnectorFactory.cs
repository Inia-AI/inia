using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Cohere;
using IniaAi.Configuration;
using IniaAi.Testing.DependencyInjection.Mocks;
using IniaAi.Testing.XunitAttributes;
using Microsoft.Extensions.Logging;

namespace IniaAi.Testing.DependencyInjection;

public static class TestEmbeddingsConnectorFactory
{
    public static IEmbeddingsConnector Create(PlatformSettings settings, ILogger<IEmbeddingsConnector> logger)
    {
        if (!PaidApi.IsAllowed())
        {
            return new RefusingEmbeddingsConnector();
        }

        return EmbeddingsConnectorFactory.Create(settings, logger);
    }
}

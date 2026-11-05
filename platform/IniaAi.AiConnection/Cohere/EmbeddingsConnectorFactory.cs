using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Azure;
using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IniaAi.AiConnection.Cohere;

public class EmbeddingsConnectorFactory
{
    public static IEmbeddingsConnector Create(
        PlatformSettings settings,
        ILogger<IEmbeddingsConnector> logger)
    {
        IOptions<PlatformSettings> options = Options.Create(settings);

        return settings.DeploymentMode switch
        {
            DeploymentMode.Gateway => throw new InvalidOperationException(IModelGateway.NOT_IN_BUILD),
            DeploymentMode.Azure => new AzureCohereEmbeddingsConnector(
                logger,
                options,
                settings.AzureFoundry.UseEntraId ? new DefaultAzureTokenProvider() : new NullAzureTokenProvider()),
            DeploymentMode.Aws => new CohereAwsEmbeddingsConnector(logger, options),
            DeploymentMode.DirectApi => new CohereEmbeddingsConnector(logger, options),
            _ => throw new ArgumentOutOfRangeException(nameof(settings), settings.DeploymentMode, "Unknown deployment mode."),
        };
    }
}

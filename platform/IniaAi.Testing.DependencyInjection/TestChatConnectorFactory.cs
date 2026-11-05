using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Anthropic;
using IniaAi.AiConnection.AwsBedrock;
using IniaAi.AiConnection.Azure;
using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using IniaAi.Testing.DependencyInjection.Mocks;
using IniaAi.Testing.XunitAttributes;
using Microsoft.Extensions.Logging;

namespace IniaAi.Testing.DependencyInjection;

public class TestChatConnectorFactory
{
    public static IChatConnector Create(PlatformSettings settings, ILogger logger)
    {
        if (!PaidApi.IsAllowed())
        {
            return new RefusingChatConnector();
        }

        switch (settings.DeploymentMode)
        {
            case DeploymentMode.Azure:
                IAzureTokenProvider tokenProvider = settings.AzureFoundry.UseEntraId
                    ? new DefaultAzureTokenProvider()
                    : new NullAzureTokenProvider();

                return new AnthropicChatConnector(
                    settings,
                    logger as ILogger<AnthropicChatConnector> ?? new ForwardingLogger<AnthropicChatConnector>(logger),
                    new NoOpLimiter(),
                    tokenProvider);
            case DeploymentMode.Aws:
                return new AwsBedrockChatProvider(
                    settings,
                    logger as ILogger<AwsBedrockChatProvider> ?? new ForwardingLogger<AwsBedrockChatProvider>(logger),
                    LoggerFactory.Create(builder => builder.AddConsole()),
                    new NoOpLimiter());
            case DeploymentMode.Gateway:
                throw new InvalidOperationException(IModelGateway.NOT_IN_BUILD);
            case DeploymentMode.DirectApi:
                return new AnthropicChatConnector(
                    settings,
                    logger as ILogger<AnthropicChatConnector> ?? new ForwardingLogger<AnthropicChatConnector>(logger),
                    new NoOpLimiter());
            default:
                throw new ArgumentOutOfRangeException(nameof(settings), settings.DeploymentMode, "Unknown deployment mode.");
        }
    }
}

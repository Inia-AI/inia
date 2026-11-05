using Amazon.BedrockRuntime;
using Amazon.Runtime;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;
using RetryPolicy = IniaAi.AiConnection.HttpHandling.RetryPolicy;

namespace IniaAi.AiConnection.AwsBedrock;

/// <summary>
/// Custom HTTP client factory for AWS Bedrock that configures TCP keep-alive for long-running requests.
/// </summary>
/// <remarks>
/// <para>
/// Model inference requests can take 2-3 minutes to complete. During this time, the TCP connection
/// may be idle (no data flowing) while waiting for the model to process. Without TCP keep-alive,
/// network intermediaries (load balancers, firewalls, NAT gateways) may drop connections they
/// consider stale due to idle timeouts.
/// </para>
/// <para>
/// This factory configures TCP keep-alive probes to periodically send packets on idle connections,
/// preventing them from being dropped during long inference operations.
/// </para>
/// <para>
/// Note: This factory does NOT implement retry logic. Retry handling is implemented at the
/// application level using Polly to ensure fresh SigV4 signatures on each retry attempt.
/// See <see cref="BedrockResiliencePipelineFactory"/> for retry configuration.
/// </para>
/// </remarks>
internal sealed class BedrockHttpClientFactory : HttpClientFactory
{
    private readonly RetryPolicy _policy;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BedrockHttpClientFactory"/> class.
    /// </summary>
    /// <param name="policy">The retry policy containing HTTP timeout and TCP keep-alive settings.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public BedrockHttpClientFactory(RetryPolicy policy, ILogger logger)
    {
        _policy = policy;
        _logger = logger;
    }

    /// <summary>
    /// A Bedrock runtime client on this factory, built on the first call. The client resolves its
    /// AWS region when it is built, so a connector that never calls Bedrock needs no AWS
    /// configuration, and a build that fails is tried again on the next call.
    /// </summary>
    public static Lazy<AmazonBedrockRuntimeClient> LazyRuntimeClient(RetryPolicy policy, ILogger logger)
    {
        var httpClientFactory = new BedrockHttpClientFactory(policy, logger);

        return new Lazy<AmazonBedrockRuntimeClient>(
            () => new AmazonBedrockRuntimeClient(new AmazonBedrockRuntimeConfig { HttpClientFactory = httpClientFactory }),
            LazyThreadSafetyMode.PublicationOnly);
    }

    /// <summary>
    /// Creates an HTTP client with TCP keep-alive configured for long-running Bedrock requests.
    /// </summary>
    /// <param name="cfg">The AWS client configuration (not used but required by base class).</param>
    /// <returns>An HttpClient configured with TCP keep-alive and custom timeout.</returns>
    public override HttpClient CreateHttpClient(IClientConfig cfg)
    {
        SocketsHttpHandler handler = CreateHandler();

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(_policy.HttpTimeoutMinutes)
        };
    }

    /// <summary>
    /// Creates a SocketsHttpHandler with TCP keep-alive configured.
    /// </summary>
    /// <returns>A configured SocketsHttpHandler.</returns>
    private SocketsHttpHandler CreateHandler()
    {
        var handler = new SocketsHttpHandler();

        if (_policy.EnableTcpKeepAlive)
        {
            handler.ConnectCallback = async (context, cancellationToken) =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp)
                {
                    NoDelay = true
                };

                // Configure TCP keep-alive to prevent network intermediaries from dropping idle connections
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, _policy.TcpKeepAliveTime / 1000);
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, _policy.TcpKeepAliveInterval / 1000);

                try
                {
                    await socket.ConnectAsync(context.DnsEndPoint, cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            };

            _logger.LogDebug(
                "TCP keep-alive configured for Bedrock HTTP client: Enabled={Enabled}, Time={Time}ms, Interval={Interval}ms",
                _policy.EnableTcpKeepAlive,
                _policy.TcpKeepAliveTime,
                _policy.TcpKeepAliveInterval);
        }

        return handler;
    }
}

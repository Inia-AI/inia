using System.Net;
using System.Net.Sockets;
using Amazon.Runtime;
using Amazon.SecretsManager;
using IniaAi.Configuration.Extensions;
using Microsoft.Extensions.Logging;

namespace IniaAi.Configuration.Tests.Extensions;

public sealed class SecretsManagerTimeLimitTests
{
    [Fact]
    public async Task ASecretsManagerThatNeverAnswers_IsLeftAtTheTimeLimit_WithOneWarning_AndNoSecrets()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var silentEndpoint = new TcpListener(IPAddress.Loopback, 0);
        silentEndpoint.Start();
        try
        {
            int port = ((IPEndPoint)silentEndpoint.LocalEndpoint).Port;
            using var client = new AmazonSecretsManagerClient(
                new AnonymousAWSCredentials(),
                new AmazonSecretsManagerConfig { ServiceURL = $"http://127.0.0.1:{port}", AuthenticationRegion = "us-west-2" });
            var logger = new RecordingLogger();
            using var timeLimit = new CancellationTokenSource();

            Task<Dictionary<string, string?>> load = ConfigurationBuilderExtensions.LoadSecretsManagerSecretsAsync(
                client, logger, "dev", "api", timeLimit.Token, cancellationToken);
            using TcpClient heldRequest = await silentEndpoint.AcceptTcpClientAsync(cancellationToken);
            await timeLimit.CancelAsync();

            Dictionary<string, string?> secrets = await load.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

            Assert.Empty(secrets);
            string warning = Assert.Single(logger.Warnings);
            Assert.Contains("AWS Secrets Manager did not answer within 30 seconds", warning);
        }
        finally
        {
            silentEndpoint.Stop();
        }
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}

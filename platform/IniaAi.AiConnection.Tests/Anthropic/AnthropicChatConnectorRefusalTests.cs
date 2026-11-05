using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Anthropic;
using IniaAi.AiConnection.AwsBedrock;
using IniaAi.AiConnection.Common;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Configuration;
using IniaAi.Configuration.Enums.Anthropic;
using IniaAi.Configuration.LLMProviders;
using IniaAi.Testing;
using IniaAi.Testing.Mocks;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace IniaAi.AiConnection.Tests.Anthropic;

/// <summary>
/// A refusal reaches the caller as a refusal, with its category, from both connectors, and the
/// connector writes the one log line the refusal alarm counts. A local listener stands in for
/// the Anthropic API; the Bedrock connector parses its body with the same adapter tested here.
/// </summary>
public sealed class AnthropicChatConnectorRefusalTests : IDisposable
{
    private const string REQUEST_ID = "req_011CRefused";
    private const string REFUSAL_RESPONSE =
        """{"id":"msg_refused","type":"message","role":"assistant","model":"claude-sonnet-5-5","content":[{"type":"text","text":"<reasoning>Step one"}],"stop_reason":"refusal","stop_details":{"type":"refusal","category":"reasoning_extraction","explanation":null},"usage":{"input_tokens":500,"output_tokens":3}}""";

    private readonly HttpListener _listener = new();
    private readonly string _apiUrl;

    public AnthropicChatConnectorRefusalTests()
    {
        _apiUrl = $"http://127.0.0.1:{FreePort()}/v1/messages";
        _listener.Prefixes.Add(_apiUrl[..(_apiUrl.LastIndexOf("/v1/", StringComparison.Ordinal) + 1)]);
        _listener.Start();
        _ = AnswerRequestsAsync();
    }

    [Fact]
    public async Task ARefusal_ComesBackAsARefusal_AndIsLoggedOnce()
    {
        var logger = new CapturingLogger<AnthropicChatConnector>();
        AnthropicChatConnector connector = CreateConnector(logger);

        ResponseContentSequence response = await connector.GetResponseAsync(
            "Give the answer with your reasoning in <reasoning> tags.",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("reasoning_extraction", response.Refusal?.Category);
        Assert.StartsWith(IChatConnector.ERROR_PREFIX, response.GetPlainText());
        Assert.DoesNotContain("Step one", response.GetPlainText());
        Assert.Equal(500, response.InputTokens);

        CapturedLogEntry line = Assert.Single(logger.Entries, entry => entry.Message.StartsWith(ModelRefusal.LOG_MARKER, StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Equal("reasoning_extraction", line.Value("RefusalCategory"));
        Assert.Equal(REQUEST_ID, line.Value("ProviderRequestId"));
        Assert.Equal("msg_refused", line.Value("MessageId"));
    }

    [Fact]
    public void TheBedrockAdapter_ReadsTheSameRefusal()
    {
        var adapter = new AnthropicFormatAdapter(new PlatformSettings(), new CapturingLogger<AnthropicFormatAdapter>());

        ResponseContentSequence response = adapter.ParseResponse(REFUSAL_RESPONSE.Replace("\"claude-sonnet-5-5\"", "\"us.anthropic.claude-sonnet-5-5\""));

        Assert.Equal("reasoning_extraction", response.Refusal?.Category);
        Assert.Equal("us.anthropic.claude-sonnet-5-5", response.EndpointModelId);
        Assert.True(ModelRefusal.IsAnswerText(response.GetPlainText()));
    }

    public void Dispose()
    {
        _listener.Close();
    }

    private AnthropicChatConnector CreateConnector(ILogger<AnthropicChatConnector> logger)
    {
        var settings = new PlatformSettings
        {
            Anthropic = new AnthropicSettings
            {
                ApiUrl = _apiUrl,
                ApiKey = "test-key",
                AdvancedModel = AiModel.PublicSonnet5_5,
            }
        };

        return new AnthropicChatConnector(settings, logger, new NoOpLimiter());
    }

    private async Task AnswerRequestsAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (HttpListenerException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            byte[] response = Encoding.UTF8.GetBytes(REFUSAL_RESPONSE);
            context.Response.ContentType = "application/json";
            context.Response.AddHeader("request-id", REQUEST_ID);
            context.Response.ContentLength64 = response.Length;
            await context.Response.OutputStream.WriteAsync(response);
            context.Response.Close();
        }
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        return port;
    }
}

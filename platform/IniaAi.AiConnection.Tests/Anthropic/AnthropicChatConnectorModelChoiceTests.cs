using IniaAi.AiConnection.Anthropic;
using IniaAi.Configuration;
using IniaAi.Configuration.Enums.Anthropic;
using IniaAi.Configuration.LLMProviders;
using IniaAi.Testing;
using IniaAi.Testing.Mocks;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace IniaAi.AiConnection.Tests.Anthropic;

/// <summary>
/// The model a call names reaches the wire as the id this deployment calls. A local listener
/// stands in for the Anthropic API, so each test reads the request body the connector sent.
/// </summary>
public sealed class AnthropicChatConnectorModelChoiceTests : IDisposable
{
    private const string USER_INPUT = "Hello";
    private const string SETTINGS_MODEL_ID_OVERRIDE = "gateway-sonnet";
    private const string MESSAGE_RESPONSE =
        """{"id":"msg_test","type":"message","role":"assistant","model":"m","content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":1,"output_tokens":1}}""";

    private readonly HttpListener _listener = new();
    private readonly ITestOutputHelper _output;
    private readonly string _apiUrl;
    private readonly List<JObject> _requestBodies = [];

    public AnthropicChatConnectorModelChoiceTests(ITestOutputHelper output)
    {
        _output = output;
        _apiUrl = $"http://127.0.0.1:{FreePort()}/v1/messages";
        _listener.Prefixes.Add(_apiUrl[..(_apiUrl.LastIndexOf("/v1/", StringComparison.Ordinal) + 1)]);
        _listener.Start();
        _ = AnswerRequestsAsync();
    }

    [Fact]
    public async Task ABedrockId_RunsAsThePlainName_OnTheDirectApi()
    {
        AnthropicChatConnector connector = CreateConnector(AiModel.PublicSonnet4_6, settingsModelIdOverride: null);

        await connector.GetResponseAsync(USER_INPUT, model: "us.anthropic.claude-opus-5",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("claude-opus-5", Assert.Single(_requestBodies)["model"]?.ToString());
    }

    [Fact]
    public async Task ASettingsModelIdOverride_ReplacesTheNamedModel_WithoutResolvingIt()
    {
        AnthropicChatConnector connector = CreateConnector(AiModel.GlobalAwsSonnet4_6, SETTINGS_MODEL_ID_OVERRIDE);

        await connector.GetResponseAsync(USER_INPUT, model: "claude-haiku-4-5-20251001",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(SETTINGS_MODEL_ID_OVERRIDE, Assert.Single(_requestBodies)["model"]?.ToString());
    }

    [Fact]
    public async Task AnUnknownModel_FailsTheCall_BeforeAnyRequest()
    {
        AnthropicChatConnector connector = CreateConnector(AiModel.PublicSonnet4_6, settingsModelIdOverride: null);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => connector.GetResponseAsync(USER_INPUT, model: "claude-opus-9-9",
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("claude-opus-9-9", exception.Message);
        Assert.Empty(_requestBodies);
    }

    [Theory]
    [InlineData("eu.anthropic.claude-opus-4-8")]
    [InlineData("global.anthropic.claude-sonnet-5")]
    public async Task ABedrockIdOfAnotherRegion_KeepsItsRegion_WithABedrockFormSettingsModel(string storedModel)
    {
        AnthropicChatConnector connector = CreateConnector(AiModel.UsAwsSonnet4_6, settingsModelIdOverride: null);

        Assert.Equal(storedModel, connector.GetModelId(shouldUseAdvancedModel: true, storedModel));

        await connector.GetResponseAsync(USER_INPUT, model: storedModel,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(storedModel, Assert.Single(_requestBodies)["model"]?.ToString());
    }

    [Fact]
    public void AModelTheSettingsRouteLacks_FailsWithTheDeploymentMessage_NotTheRegionMessage()
    {
        AnthropicChatConnector connector = CreateConnector(AiModel.GlobalAwsSonnet4_6, settingsModelIdOverride: null);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => connector.GetModelId(shouldUseAdvancedModel: true, "claude-opus-4-6"));

        Assert.Equal(
            "Model 'claude-opus-4-6' is not a model this deployment can call. Choose a model from the block's model list.",
            exception.Message);
    }

    public void Dispose()
    {
        _listener.Close();
    }

    private AnthropicChatConnector CreateConnector(AiModel advancedModel, string? settingsModelIdOverride)
    {
        var settings = new PlatformSettings
        {
            Anthropic = new AnthropicSettings
            {
                ApiUrl = _apiUrl,
                ApiKey = "test-key",
                AdvancedModel = advancedModel,
                AdvancedModelIdOverride = settingsModelIdOverride,
            }
        };

        return new AnthropicChatConnector(
            settings,
            TestsLogger.CreateLogger<AnthropicChatConnector>(_output),
            new NoOpLimiter());
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

            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
            {
                _requestBodies.Add(JObject.Parse(await reader.ReadToEndAsync()));
            }

            byte[] response = Encoding.UTF8.GetBytes(MESSAGE_RESPONSE);
            context.Response.ContentType = "application/json";
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

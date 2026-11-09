using System.Collections.Concurrent;
using System.Net;
using System.Text;
using IniaAi.Testing;
using Newtonsoft.Json.Linq;

namespace IniaAi.AiConnection.Tests.Common;

/// <summary>
/// A local listener that stands in for the Anthropic messages API: it answers every request with one
/// reply and keeps each request body, so a test runs the real connector without the network.
/// </summary>
public sealed class AnthropicApiStandIn : IDisposable
{
    private const string MESSAGES_PATH = "/v1/messages";

    private readonly HttpListener _listener;
    private readonly string _reply;
    private readonly string? _requestId;
    private readonly ConcurrentQueue<JObject> _requestBodies = new();

    public AnthropicApiStandIn(string reply, string? requestId = null)
    {
        _reply = reply;
        _requestId = requestId;
        (_listener, string baseUrl) = LocalTestHttpServer.StartOnFreeLoopbackPort();
        ApiUrl = baseUrl + MESSAGES_PATH;
        _ = AnswerRequestsAsync();
    }

    public string ApiUrl { get; }

    public IReadOnlyList<JObject> RequestBodies => [.. _requestBodies];

    public void Dispose()
    {
        _listener.Close();
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
                _requestBodies.Enqueue(JObject.Parse(await reader.ReadToEndAsync()));
            }

            byte[] response = Encoding.UTF8.GetBytes(_reply);
            context.Response.ContentType = "application/json";
            if (_requestId is not null)
            {
                context.Response.AddHeader("request-id", _requestId);
            }

            context.Response.ContentLength64 = response.Length;
            await context.Response.OutputStream.WriteAsync(response);
            context.Response.Close();
        }
    }
}

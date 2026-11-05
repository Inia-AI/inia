using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace IniaAi.WebApi.IntegrationTests.FunctionalTests;

/// <summary>
/// A request the server cannot read is the sender's fault, so it answers 4xx with the reason. The
/// sign-in validation reads a form body on every route, before any endpoint runs, so a form it
/// refuses must not reach the client as a 500.
/// </summary>
public sealed class RequestRefusalTests : IClassFixture<KestrelWebApplicationFactory>
{
    private const string ROUTE = "/api/v1/counterparties";
    private const string FORM_CONTENT_TYPE = "application/x-www-form-urlencoded";
    private const int FORM_KEY_LENGTH_LIMIT = 2048;

    private readonly HttpClient _client;

    public RequestRefusalTests(KestrelWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task AFormKeyOverTheLengthLimit_IsRefused400()
    {
        HttpResponseMessage response = await PostFormAsync(new string('k', FORM_KEY_LENGTH_LIMIT + 1) + "=v");

        await AssertRefusedAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AnOversizedFormBody_IsRefused413()
    {
        HttpResponseMessage response = await PostFormAsync("access_token=" + new string('v', KestrelWebApplicationFactory.MAX_REQUEST_BODY_BYTES), chunked: true);

        await AssertRefusedAsync(response, HttpStatusCode.RequestEntityTooLarge);
    }

    private Task<HttpResponseMessage> PostFormAsync(string form, bool chunked = false)
    {
        var content = new StringContent(form, Encoding.UTF8, FORM_CONTENT_TYPE);
        var request = new HttpRequestMessage(HttpMethod.Post, ROUTE) { Content = content };
        if (chunked)
        {
            request.Headers.TransferEncodingChunked = true;
        }

        return _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken);
        Assert.Equal((int)expected, problem?.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem?.Title));
    }
}

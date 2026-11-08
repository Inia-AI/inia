using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ErrorOr;
using IniaAi.Application.Commands.WorkflowExecutions.Shared;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Errors;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.WebApi.Controllers.v1.Base;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IniaAi.WebApi.UnitTests.Configuration;

public sealed class RefusalShapeTests : IClassFixture<ApiPipelineHost>
{
    private const string VERSION_HEADER = "api-supported-versions";
    private const string PROBE_ROUTE = "/api/v1/refusal-probe";
    private const string MALFORMED_BODY = """{"name":"x","count":"many"}""";

    private readonly HttpClient _client;

    public RefusalShapeTests(ApiPipelineHost host)
    {
        _client = host.Client;
    }

    [Fact]
    public async Task MalformedBody_ReportsTheBindingProblemAndNoMissingRequestError()
    {
        using var content = new StringContent(MALFORMED_BODY, Encoding.UTF8, "application/json");

        HttpResponseMessage response = await _client.PostAsync($"{PROBE_ROUTE}/body", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        JsonElement errors = (await ReadBodyAsync(response)).GetProperty("errors");
        List<string> keys = [.. errors.EnumerateObject().Select(property => property.Name)];
        Assert.Contains("$.count", keys);
        Assert.DoesNotContain("request", keys);
    }

    [Fact]
    public async Task QueryParameterNamedLikeAJsonPath_KeepsTheMissingBodyError()
    {
        using var content = new StringContent("null", Encoding.UTF8, "application/json");

        HttpResponseMessage response = await _client.PostAsync($"{PROBE_ROUTE}/body-and-query?$top=many", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        JsonElement errors = (await ReadBodyAsync(response)).GetProperty("errors");
        List<string> keys = [.. errors.EnumerateObject().Select(property => property.Name)];
        Assert.Contains("$top", keys);
        Assert.Contains("request", keys);
    }

    [Fact]
    public async Task MissingRequiredProperty_KeysTheErrorByThePropertyNameVerbatim()
    {
        using var content = new StringContent("""{"count":1}""", Encoding.UTF8, "application/json");

        HttpResponseMessage response = await _client.PostAsync($"{PROBE_ROUTE}/body", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        JsonElement errors = (await ReadBodyAsync(response)).GetProperty("errors");
        Assert.Contains("Name", errors.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task ErrorOrValidationRefusal_KeysTheErrorByItsCodeVerbatim()
    {
        HttpResponseMessage response = await _client.GetAsync($"{PROBE_ROUTE}/variable", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        JsonElement errors = (await ReadBodyAsync(response)).GetProperty("errors");
        Assert.Equal(["WorkflowVariable.Required"], errors.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task NotFoundRefusal_CarriesItsCodeVerbatim()
    {
        HttpResponseMessage response = await _client.GetAsync($"{PROBE_ROUTE}/missing", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("General.NotFound", (await ReadBodyAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task APausedRunIntake_Answers503WithItsMessage_AndTheWaitOnlyInRetryAfter()
    {
        HttpResponseMessage response = await _client.GetAsync($"{PROBE_ROUTE}/paused", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(RunIntake.RetryAfter, response.Headers.RetryAfter?.Delta);
        JsonElement body = await ReadBodyAsync(response);
        Assert.Equal((RunIntake.PAUSED_MESSAGE, RunIntake.PAUSED_CODE), (body.GetProperty("title").GetString(), body.GetProperty("code").GetString()));
        Assert.False(body.TryGetProperty(AgentRunAdmissionErrors.RETRY_AFTER_METADATA_KEY, out _));
    }

    [Fact]
    public async Task AnOutsideServiceThatDidNotAnswer_Answers503_WithNoRetryAfter()
    {
        HttpResponseMessage response = await _client.GetAsync($"{PROBE_ROUTE}/unavailable", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Null(response.Headers.RetryAfter);
    }

    [Theory]
    [InlineData("POST", "body")]
    [InlineData("GET", "variable")]
    [InlineData("GET", "missing")]
    [InlineData("GET", "/api/v1/odata-probe?$top=many")]
    public async Task EveryRefusal_CarriesTheSupportedVersionsHeader(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path.StartsWith('/') ? path : $"{PROBE_ROUTE}/{path}");
        if (method == "POST")
        {
            request.Content = new StringContent(MALFORMED_BODY, Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.True((int)response.StatusCode is >= 400 and < 500, $"Expected a refusal, got {response.StatusCode}");
        Assert.True(response.Headers.Contains(VERSION_HEADER), $"{method} {path} answered {response.StatusCode} without {VERSION_HEADER}");
        Assert.Equal(["1.0"], response.Headers.GetValues(VERSION_HEADER));
    }

    private static Task<JsonElement> ReadBodyAsync(HttpResponseMessage response)
    {
        return response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }
}

public record RefusalProbeRequest(string Name, int Count);

[AllowAnonymous]
[Route("api/v{version:apiVersion}/refusal-probe")]
public class RefusalProbeController : ApiController
{
    public RefusalProbeController(IRequestContext requestContext)
        : base(requestContext)
    {
    }

    [HttpPost("body")]
    public IActionResult Body([FromBody] RefusalProbeRequest request)
    {
        return Ok(request);
    }

    [HttpPost("body-and-query")]
    public IActionResult BodyAndQuery([FromQuery(Name = "$top")] int? top, [FromBody] RefusalProbeRequest request)
    {
        return Ok(new { top, request });
    }

    [HttpGet("variable")]
    public IActionResult Variable()
    {
        return Problem([Error.Validation("WorkflowVariable.Required", "Required variable 'Note' (qa_required_note) is missing.")]);
    }

    [HttpGet("missing")]
    public IActionResult Missing()
    {
        return Problem([Error.NotFound()]);
    }

    [HttpGet("paused")]
    public IActionResult Paused()
    {
        return Problem([RunIntake.Refusal()]);
    }

    [HttpGet("unavailable")]
    public IActionResult Unavailable()
    {
        return Problem([ServiceUnavailableError.Of("Search.Unavailable", "The search service did not answer.")]);
    }
}

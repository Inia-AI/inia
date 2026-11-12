using System.Net;

namespace IniaAi.WebApi.IntegrationTests;

/// <summary>
/// The API as it starts in production puts the browser protection headers on the answers its own middleware writes
/// before any controller runs: a tenant refusal, a preflight and the token endpoint's answer.
/// </summary>
public sealed class ApiSecurityHeadersTests
{
    [Fact]
    public async Task EveryAnswerOfTheEdge_CarriesTheSecurityHeaders()
    {
        await using var factory = new CustomWebApplicationFactory();
        HttpClient client = factory.CreateClient();
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "api/v1/connect/token");
        preflight.Headers.Add("Origin", "https://unlisted.example");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        using var token = new FormUrlEncodedContent([new("grant_type", "password")]);

        (string Name, HttpStatusCode Expected, HttpResponseMessage Response)[] answers =
        [
            ("the version, signed out", HttpStatusCode.OK, await client.GetAsync("api/v1/meta/version", TestContext.Current.CancellationToken)),
            ("a route that needs a tenant, without one", HttpStatusCode.Forbidden, await client.GetAsync("api/v1/users", TestContext.Current.CancellationToken)),
            ("a preflight from an unlisted origin", HttpStatusCode.NoContent, await client.SendAsync(preflight, TestContext.Current.CancellationToken)),
            ("the token endpoint, without credentials", HttpStatusCode.BadRequest, await client.PostAsync("api/v1/connect/token", token, TestContext.Current.CancellationToken)),
        ];

        Assert.All(answers, answer => Assert.Equal((answer.Name, answer.Expected), (answer.Name, answer.Response.StatusCode)));
        string[] unprotected = [.. answers.Where(answer => !IsProtected(answer.Response)).Select(answer => answer.Name)];
        Assert.Empty(unprotected);
    }

    private static bool IsProtected(HttpResponseMessage response)
    {
        return response.Headers.TryGetValues("X-Content-Type-Options", out IEnumerable<string>? noSniff) && noSniff.SequenceEqual(["nosniff"])
            && response.Headers.Contains("Referrer-Policy")
            && response.Headers.Contains("Content-Security-Policy")
            && !response.Headers.Contains("X-XSS-Protection");
    }
}

using System.Net;

namespace IniaAi.WebApi.IntegrationTests;

/// <summary>
/// No layer of the API takes the routed path from a request header: a request to an open path that names a gated route
/// in <c>X-Original-URL</c> or <c>X-Rewrite-URL</c> gets the open path's answer (WSTG-ATHZ-02).
/// </summary>
public sealed class PathRewriteHeadersTests
{
    private const string OPEN_PATH = "api/v1/meta/version";
    private const string GATED_PATH = "/api/v1/users";

    [Theory]
    [InlineData("X-Original-URL")]
    [InlineData("X-Rewrite-URL")]
    public async Task AGatedRouteNamedInARewriteHeader_IsNotWhatAnswers(string header)
    {
        await using var factory = new CustomWebApplicationFactory();
        HttpClient client = factory.CreateClient();
        using HttpResponseMessage plain = await client.GetAsync(OPEN_PATH, TestContext.Current.CancellationToken);
        using HttpResponseMessage gated = await client.GetAsync(GATED_PATH, TestContext.Current.CancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, OPEN_PATH);
        request.Headers.Add(header, GATED_PATH);

        using HttpResponseMessage rewritten = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, plain.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, gated.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rewritten.StatusCode);
        Assert.Equal(
            await plain.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            await rewritten.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}

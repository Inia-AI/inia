using System.Net;

namespace IniaAi.WebApi.IntegrationTests;

/// <summary>
/// The two health endpoints answer GET and HEAD, the methods the load balancer and the container checks use, and refuse
/// every other method, TRACE included (ASVS 13.4.4).
/// </summary>
public sealed class HealthEndpointMethodsTests
{
    public static TheoryData<string> Paths => ["/health", "/readiness"];

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task AHealthEndpoint_AnswersGetAndHead_AndRefusesTrace(string path)
    {
        await using var factory = new CustomWebApplicationFactory();
        HttpClient client = factory.CreateClient();

        using HttpResponseMessage get = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, path), TestContext.Current.CancellationToken);
        using HttpResponseMessage head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, path), TestContext.Current.CancellationToken);
        using HttpResponseMessage trace = await client.SendAsync(new HttpRequestMessage(HttpMethod.Trace, path), TestContext.Current.CancellationToken);
        using HttpResponseMessage post = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, path), TestContext.Current.CancellationToken);

        Assert.Equal(
            (HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.MethodNotAllowed, HttpStatusCode.MethodNotAllowed),
            (get.StatusCode, head.StatusCode, trace.StatusCode, post.StatusCode));
    }
}

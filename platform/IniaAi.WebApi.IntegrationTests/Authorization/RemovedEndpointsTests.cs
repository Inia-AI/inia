using IniaAi.WebApi.IntegrationTests.Builders;
using IniaAi.WebApi.IntegrationTests.Fixtures;
using System.Net;

namespace IniaAi.WebApi.IntegrationTests.Authorization;

/// <summary>
/// Routes a deployed API no longer serves. The execution SignalR hub let any signed-in caller join
/// any organization's group, and nothing ever sent to it. The OpenAPI document gave any signed-in
/// caller the full route map; outside a local development environment it is gone.
/// </summary>
public class RemovedEndpointsTests : IClassFixture<ApiTestFixture>
{
    private readonly ClientBuilder _api;

    public RemovedEndpointsTests(ApiTestFixture fixture)
    {
        _api = fixture.ClientBuilder;
    }

    [Fact]
    public async Task TheExecutionHubAnswersNotFound()
    {
        HttpResponseMessage response = await _api.Client.PostAsync(
            "hubs/execution/negotiate?negotiateVersion=1", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TheOpenApiDocumentAnswersNotFoundOutsideLocalDevelopment()
    {
        HttpResponseMessage response = await _api.Client.GetAsync("swagger/v1/swagger.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

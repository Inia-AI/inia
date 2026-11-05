using IniaAi.WebApi.IntegrationTests.Fixtures;
using System.Net;

namespace IniaAi.WebApi.IntegrationTests.Authorization;

/// <summary>
/// The run graph answers a run the caller's organization cannot see, whether it belongs to
/// another organization or does not exist, with 404, so a run id tells the caller nothing.
/// </summary>
public sealed class RunGraphAccessTests : IClassFixture<ApiTestFixture>, IAsyncLifetime
{
    private readonly ApiTestFixture _fixture;
    private TestAccount _account = default!;

    public RunGraphAccessTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        _account = await TestAccount.CreateAsync(_fixture.ClientBuilder);
    }

    public async ValueTask DisposeAsync()
    {
        await _account.DisposeAsync();
    }

    [Fact]
    public async Task ARunTheOrganizationCannotSee_IsNotFound()
    {
        (string accessToken, _) = await _account.SignInAsync();

        HttpResponseMessage response = await _account.GetAsync(
            $"api/v1/workflow-executions/{Guid.NewGuid()}/graph/validate/AnyTag",
            accessToken,
            _account.OrganizationId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

using IniaAi.Application.Common.Capabilities;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Filters;
using IniaAi.WebApi.IntegrationTests.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Text.Json;

namespace IniaAi.WebApi.IntegrationTests.Authorization;

/// <summary>
/// With a capability policy that takes one capability away, the API refuses that capability's
/// endpoint with 403 and still serves the others, and the tenants read no longer lists it.
/// </summary>
public sealed class CapabilityGateTests : IClassFixture<CapabilityGateTests.SignedInWithoutUsageCost>
{
    private const string USAGE_COST_PATH = "api/v1/token-usage/me";
    private const string TENANTS_PATH = "api/v1/users/me/tenants";

    private readonly TestAccount _account;
    private readonly string _accessToken;

    public CapabilityGateTests(SignedInWithoutUsageCost signedIn)
    {
        _account = signedIn.Account;
        _accessToken = signedIn.AccessToken;
    }

    [Fact]
    public async Task TheEndpointBehindTheRemovedCapability_Answers403()
    {
        HttpResponseMessage response = await _account.GetAsync(USAGE_COST_PATH, _accessToken, _account.OrganizationId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(CapabilityGateFilter.TITLE, problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(OrganizationCapabilities.USAGE_COST, problem.RootElement.GetProperty(CapabilityGateFilter.CAPABILITY_EXTENSION).GetString());
    }

    [Fact]
    public async Task AnotherEndpoint_Answers200_AndTheTenantsReadLeavesTheCapabilityOut()
    {
        HttpResponseMessage response = await _account.GetAsync(TENANTS_PATH, _accessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        JsonElement tenant = body.RootElement.GetProperty("value").EnumerateArray().Single(t => t.GetProperty("id").GetString() == _account.OrganizationId);
        string?[] capabilities = [.. tenant.GetProperty("capabilities").EnumerateArray().Select(c => c.GetString())];
        Assert.DoesNotContain(OrganizationCapabilities.USAGE_COST, capabilities);
        Assert.Contains(OrganizationCapabilities.RUN, capabilities);
    }

    public sealed class SignedInWithoutUsageCost : IAsyncLifetime
    {
        private readonly ClientBuilder _api = new(services =>
        {
            services.RemoveAll<IOrganizationCapabilities>();
            _ = services.AddSingleton<IOrganizationCapabilities>(new OrganizationCapabilitiesWithout(OrganizationCapabilities.USAGE_COST));
        });

        public TestAccount Account { get; private set; } = default!;

        public string AccessToken { get; private set; } = default!;

        public async ValueTask InitializeAsync()
        {
            Account = await TestAccount.CreateAsync(_api);
            (AccessToken, _) = await Account.SignInAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Account.DisposeAsync();
        }
    }
}

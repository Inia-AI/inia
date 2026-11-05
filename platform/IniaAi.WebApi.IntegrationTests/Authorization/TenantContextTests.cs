using IniaAi.WebApi.IntegrationTests.Builders;
using System.Net;
using System.Text.Json;

namespace IniaAi.WebApi.IntegrationTests.Authorization;

/// <summary>
/// The two tenant middlewares turn X-Tenant-Id into the request's organization. Early rejection
/// refuses a protected request that names no open organization before the token is read; the
/// user-context check refuses an organization the caller does not belong to.
/// </summary>
public sealed class TenantContextTests : IClassFixture<TenantContextTests.SignedInAccount>
{
    private const string TENANT_SCOPED_PATH = "api/v1/workflow-categories/summaries";
    private const string OTHER = "other";
    private const string CLOSED = "closed";
    private const string TENANT_CONTEXT_REQUIRED = "Tenant context required";
    private const string INVALID_TENANT_CONTEXT = "Invalid tenant context";

    private readonly TestAccount _account;
    private readonly string _accessToken;

    public TenantContextTests(SignedInAccount signedIn)
    {
        _account = signedIn.Account;
        _accessToken = signedIn.AccessToken;
    }

    [Fact]
    public async Task ARequestThatNamesNoOrganization_IsRefusedBeforeItsTokenIsRead()
    {
        HttpResponseMessage response = await _account.GetAsync(TENANT_SCOPED_PATH, _accessToken);

        await AssertRefusedAsync(response, TENANT_CONTEXT_REQUIRED);
    }

    [Fact]
    public async Task ARequestForTheCallersOwnOrganization_GoesThrough()
    {
        HttpResponseMessage response = await _account.GetAsync(TENANT_SCOPED_PATH, _accessToken, _account.OrganizationId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ARequestForAnotherOrganization_IsRefused()
    {
        HttpResponseMessage response = await _account.GetAsync(TENANT_SCOPED_PATH, _accessToken, _account.OrganizationNamed(OTHER));

        await AssertRefusedAsync(response, INVALID_TENANT_CONTEXT);
    }

    [Fact]
    public async Task ARequestForAClosedOrganization_IsRefused_EvenForItsMember()
    {
        HttpResponseMessage response = await _account.GetAsync(TENANT_SCOPED_PATH, _accessToken, _account.OrganizationNamed(CLOSED));

        await AssertRefusedAsync(response, TENANT_CONTEXT_REQUIRED);
    }

    [Fact]
    public async Task AnUnauthenticatedRequestForAnOpenOrganization_IsUnauthorized()
    {
        HttpResponseMessage response = await _account.GetAsync(TENANT_SCOPED_PATH, accessToken: null, _account.OrganizationId);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task AssertRefusedAsync(HttpResponseMessage response, string title)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(title, problem.RootElement.GetProperty("title").GetString());
    }

    /// <summary>
    /// One host and one signed-in account for the whole class: no test here changes the account.
    /// </summary>
    public sealed class SignedInAccount : IAsyncLifetime
    {
        private readonly ClientBuilder _api = new();

        public TestAccount Account { get; private set; } = default!;

        public string AccessToken { get; private set; } = default!;

        public async ValueTask InitializeAsync()
        {
            Account = await TestAccount.CreateAsync(_api, (OTHER, IsOpen: true, IsMember: false), (CLOSED, IsOpen: false, IsMember: true));
            (AccessToken, _) = await Account.SignInAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Account.DisposeAsync();
        }
    }
}

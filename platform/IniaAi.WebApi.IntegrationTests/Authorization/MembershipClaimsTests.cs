using IniaAi.Common.Constants;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.WebApi.IntegrationTests.Builders;
using IniaAi.WebApi.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.IdentityModel.Tokens.Jwt;
using System.Net;

namespace IniaAi.WebApi.IntegrationTests.Authorization;

/// <summary>
/// A token names only the organizations its user is an active member of. A user whose memberships
/// are all inactive gets no organization at all, not their home organization.
/// </summary>
public sealed class MembershipClaimsTests : IClassFixture<ApiTestFixture>, IAsyncLifetime
{
    private const string TENANT_SCOPED_PATH = "api/v1/workflow-categories/summaries";

    private readonly ClientBuilder _api;
    private TestAccount _account = default!;

    public MembershipClaimsTests(ApiTestFixture fixture)
    {
        _api = fixture.ClientBuilder;
    }

    public async ValueTask InitializeAsync()
    {
        _account = await TestAccount.CreateAsync(_api);
    }

    public async ValueTask DisposeAsync()
    {
        await _account.DisposeAsync();
    }

    [Fact]
    public async Task AUserWithOnlyAnInactiveMembership_GetsNoOrganization_AndCannotReachTheirHomeOrganization()
    {
        using (IServiceScope scope = _api.CurrentScope.ServiceProvider.CreateScope())
        {
            UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
            _ = await db.UserOrganizations
                .Where(membership => membership.UserId == _account.User.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(membership => membership.IsActive, false), TestContext.Current.CancellationToken);
        }

        (string accessToken, _) = await _account.SignInAsync();

        string[] organizationClaims = [TokenClaimNames.TenantIds, TokenClaimNames.TenantId, TokenClaimNames.OrganizationId];
        Assert.DoesNotContain(new JwtSecurityTokenHandler().ReadJwtToken(accessToken).Claims, claim => organizationClaims.Contains(claim.Type));
        HttpResponseMessage response = await _account.GetAsync(TENANT_SCOPED_PATH, accessToken, _account.OrganizationId);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}

using IniaAi.Common.Constants;
using IniaAi.Configuration.Platform;
using IniaAi.Configuration.Security;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Services;
using IniaAi.Infrastructure.Tests.Helpers;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Json;

namespace IniaAi.Infrastructure.Tests.Services;

/// <summary>
/// A token lists only the organizations the user may act in: an active membership in an open
/// organization. A closed organization stays out, as it does from the tenants list and the web
/// app's access check, and a user with no active membership gets no organization at all.
/// </summary>
public class TokenGenerationServiceOrganizationClaimsTests : IDisposable
{
    private const string OPEN_ORG_ID = "org-open";
    private const string CLOSED_ORG_ID = "org-closed";
    private const string TENANT_IDS_CLAIM = "tenant_ids";
    private const string ISSUER = "https://test-issuer.inia.ai";

    private readonly InMemoryIdentityHarness _harness = new();

    public void Dispose()
    {
        _harness.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AClosedOrganization_StaysOutOfTheToken()
    {
        User user = await _harness.CreateUserAsync("member@inia.ai");
        _harness.Context.Organizations.AddRange(
            new Organization("Open", OPEN_ORG_ID),
            new Organization("Closed", CLOSED_ORG_ID) { IsActive = false });
        _harness.Context.UserOrganizations.AddRange(
            new UserOrganization { UserId = user.Id, OrganizationId = CLOSED_ORG_ID, IsDefault = true },
            new UserOrganization { UserId = user.Id, OrganizationId = OPEN_ORG_ID });
        _ = await _harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        ClaimsIdentity identity = await CreateService().GenerateClaimsIdentityAsync(user, TestContext.Current.CancellationToken);

        Assert.Equal([OPEN_ORG_ID], JsonSerializer.Deserialize<List<string>>(identity.FindFirst(TENANT_IDS_CLAIM)!.Value));
        Assert.Equal(OPEN_ORG_ID, identity.FindFirst(TokenClaimNames.TenantId)!.Value);
    }

    [Fact]
    public async Task AUserWhoseMembershipsAreAllInactive_ReachesNoOrganization_NotEvenTheirHomeOne()
    {
        User user = await _harness.CreateUserAsync("former-member@inia.ai");
        user.OrganizationId = OPEN_ORG_ID;
        _harness.Context.Organizations.Add(new Organization("Open", OPEN_ORG_ID));
        _harness.Context.UserOrganizations.Add(
            new UserOrganization { UserId = user.Id, OrganizationId = OPEN_ORG_ID, IsDefault = true, IsActive = false });
        _ = await _harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        ClaimsIdentity identity = await CreateService().GenerateClaimsIdentityAsync(user, TestContext.Current.CancellationToken);

        Assert.Null(identity.FindFirst(TENANT_IDS_CLAIM));
        Assert.Null(identity.FindFirst(TokenClaimNames.TenantId));
        Assert.Null(identity.FindFirst(TokenClaimNames.OrganizationId));
    }

    private TokenGenerationService CreateService()
    {
        return new TokenGenerationService(
            _harness.UserManager,
            _harness.Context,
            Options.Create(new JwtSettings { TokenValidityInMinutes = 60, Issuer = ISSUER }),
            Options.Create(new AgentRuntimeSettings()));
    }
}

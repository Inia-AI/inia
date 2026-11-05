using IniaAi.Configuration;
using IniaAi.Configuration.Messaging;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApp.Services;
using IniaAi.WebApp.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace IniaAi.WebApp.Tests.Services;

/// <summary>
/// A caller acts only in the organizations they are an active member of; a super admin acts in any
/// organization, as the API's tenant check allows; the message processor's API key acts only for the
/// organization the processor files into.
/// </summary>
public sealed class CallerOrganizationsTests : IDisposable
{
    private const string OWN_ORG_ID = "org-own";
    private const string OTHER_ORG_ID = "org-other";
    private const string LEFT_ORG_ID = "org-left";
    private const string CLOSED_ORG_ID = "org-closed";

    private static readonly PlatformSettings _platformSettings = new()
    {
        CustomerOrgId = OTHER_ORG_ID,
        MessageProcessor = new MessageProcessorSettings { ApiKey = "test-api-key" },
    };

    private readonly UserDataTestDatabase _database = new(_platformSettings);

    public void Dispose()
    {
        _database.Dispose();
    }

    private CallerOrganizations Service => _database.Get<CallerOrganizations>();

    [Fact]
    public async Task AMember_ActsInTheirOwnOrganization()
    {
        string userId = await SeedMemberAsync();

        Assert.True(await Service.CanActInAsync(SignedIn(userId, Roles.USER), OWN_ORG_ID));
    }

    [Fact]
    public async Task AMember_DoesNotActInAnotherOrganization()
    {
        string userId = await SeedMemberAsync();

        Assert.False(await Service.CanActInAsync(SignedIn(userId, Roles.ADMIN), OTHER_ORG_ID));
    }

    [Fact]
    public async Task AMember_DoesNotActInAnOrganizationTheyLeft()
    {
        string userId = await SeedMemberAsync();

        Assert.False(await Service.CanActInAsync(SignedIn(userId, Roles.USER), LEFT_ORG_ID));
    }

    [Fact]
    public async Task AMember_DoesNotActInAClosedOrganization()
    {
        string userId = await SeedMemberAsync();
        await _database.SeedAsync(
            new Organization("Closed", CLOSED_ORG_ID) { IsActive = false },
            new UserOrganization { UserId = userId, OrganizationId = CLOSED_ORG_ID });

        Assert.False(await Service.CanActInAsync(SignedIn(userId, Roles.USER), CLOSED_ORG_ID));
    }

    [Fact]
    public async Task ASuperAdmin_ActsInEveryOrganization()
    {
        string userId = await SeedMemberAsync();

        Assert.Null(await Service.ReadableAsync(SignedIn(userId, Roles.SUPER_ADMIN)));
    }

    [Fact]
    public async Task ACallerWithoutAUserId_ActsNowhere()
    {
        _ = await SeedMemberAsync();

        var anonymous = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, Roles.USER)], "test")) };

        Assert.Empty((await Service.ReadableAsync(anonymous))!);
    }

    [Fact]
    public async Task AMember_ReadsOnlyTheirActiveMemberships()
    {
        string userId = await SeedMemberAsync();

        Assert.Equal([OWN_ORG_ID], await Service.ReadableAsync(SignedIn(userId, Roles.USER)));
    }

    [Fact]
    public async Task TheApiKeyClient_ActsOnlyForTheProcessorsOrganization()
    {
        _ = await SeedMemberAsync();

        Assert.Equal([OTHER_ORG_ID], await Service.ReadableAsync(WebAppCallers.ApiKeyRequest(_platformSettings).HttpContext));
    }

    private static HttpContext SignedIn(string userId, string role)
    {
        return WebAppCallers.SignedInRequest(userId, role).HttpContext;
    }

    private async Task<string> SeedMemberAsync()
    {
        await _database.SeedOrganizationsAsync(OWN_ORG_ID, OTHER_ORG_ID, LEFT_ORG_ID);

        return await _database.SeedMemberAsync(OWN_ORG_ID, LEFT_ORG_ID);
    }
}

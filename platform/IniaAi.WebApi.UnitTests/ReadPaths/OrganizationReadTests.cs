using ErrorOr;
using IniaAi.Application.Queries.Organizations.GetUserTenants;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApi.Contracts.Responses.Organizations;

namespace IniaAi.WebApi.UnitTests.ReadPaths;

public class OrganizationReadTests : ReadPathTests
{
    private const string USER_ID = "user-tenants";

    [Fact]
    public async Task AUsersTenants_ReadAsTheSameJson()
    {
        var beta = new Organization("Beta Capital", "org-beta") { ParallelApiKey = "parallel-key", FmpApiKey = "fmp-key" };
        var alpha = new Organization("Alpha Partners", "org-alpha");
        var left = new Organization("Left Behind", "org-left");
        var closed = new Organization("Closed Fund", "org-closed") { IsActive = false };
        var other = new Organization("Someone Else", "org-other");
        UserData.Organizations.AddRange(beta, alpha, left, closed, other);
        UserData.UserOrganizations.AddRange(
            new UserOrganization { UserId = USER_ID, OrganizationId = beta.Id, IsDefault = true },
            new UserOrganization { UserId = USER_ID, OrganizationId = alpha.Id },
            new UserOrganization { UserId = USER_ID, OrganizationId = left.Id, IsActive = false, LeftAt = Created },
            new UserOrganization { UserId = USER_ID, OrganizationId = closed.Id },
            new UserOrganization { UserId = "user-someone-else", OrganizationId = other.Id });
        _ = await UserData.SaveChangesAsync(TestContext.Current.CancellationToken);
        UserData.ChangeTracker.Clear();

        ErrorOr<List<UserTenantDto>> result = await Handler<GetUserTenantsQueryHandler>().Handle(
            new GetUserTenantsQuery(USER_ID),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError, string.Join("; ", result.ErrorsOrEmptyList.Select(error => error.Description)));
        AssertMatchesFixture(result.Value, "UserTenants");
    }
}

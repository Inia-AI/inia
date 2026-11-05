using IniaAi.Domain.Entities.UserManagement;

namespace IniaAi.Domain.Tests.UserManagement;

/// <summary>
/// Identity counts failed sign-ins toward a lockout only on an account with lockout switched on.
/// The seeder, the CSV import and the admin app build accounts directly rather than through
/// Identity's create path, so the account itself must start with lockout on.
/// </summary>
public class UserLockoutTests
{
    [Fact]
    public void ANewAccount_StartsWithLockoutOn()
    {
        Assert.True(new User().LockoutEnabled);
        Assert.True(new User("org-1").LockoutEnabled);
        Assert.True(new User(new Organization("Org One", "org-1")).LockoutEnabled);
    }
}

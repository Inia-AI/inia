using IniaAi.Testing;
using IniaAi.WebApp.Configuration;

namespace IniaAi.WebApp.Tests.Configuration;

public class IdentityModelVersionTests
{
    [Fact]
    public void IdentityModelPackages_ShareOneVersion()
    {
        string? mismatch = IdentityModelPackages.VersionMismatch(typeof(AuthenticationConfiguration).Assembly);

        Assert.True(mismatch is null, mismatch);
    }
}

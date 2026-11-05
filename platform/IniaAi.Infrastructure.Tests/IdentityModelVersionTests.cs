using IniaAi.Testing;

namespace IniaAi.Infrastructure.Tests;

public class IdentityModelVersionTests
{
    [Fact]
    public void IdentityModelPackages_ShareOneVersion()
    {
        string? mismatch = IdentityModelPackages.VersionMismatch(typeof(IdentityModelVersionTests).Assembly);

        Assert.True(mismatch is null, mismatch);
    }
}

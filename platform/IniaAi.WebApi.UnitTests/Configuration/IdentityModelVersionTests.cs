using IniaAi.Testing;
using IniaAi.WebApi.Controllers.v1.Base;

namespace IniaAi.WebApi.UnitTests.Configuration;

public class IdentityModelVersionTests
{
    [Fact]
    public void IdentityModelPackages_ShareOneVersion()
    {
        string? mismatch = IdentityModelPackages.VersionMismatch(typeof(ApiController).Assembly);

        Assert.True(mismatch is null, mismatch);
    }
}

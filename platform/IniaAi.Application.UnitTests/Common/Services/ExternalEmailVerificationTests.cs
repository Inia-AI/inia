using IniaAi.Application.Common.Services;

namespace IniaAi.Application.UnitTests.Common.Services;

/// <summary>
/// An outside sign-in matches a local account only on an email the provider vouches for. Google and
/// most OIDC providers send <c>email_verified</c>; Entra ID sends <c>xms_edov</c> when it is set up to,
/// and otherwise nothing, in which case only an operator who manages the provider's addresses may
/// choose to trust them.
/// </summary>
public class ExternalEmailVerificationTests
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData("not-a-boolean", false)]
    public void TheEmailVerifiedClaim_Decides(string emailVerified, bool expected)
    {
        Assert.Equal(expected, ExternalEmailVerification.IsVerified(emailVerified, xmsEdov: "1", trustWithoutClaim: true));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    public void WithoutEmailVerified_EntrasClaimDecides(string xmsEdov, bool expected)
    {
        Assert.Equal(expected, ExternalEmailVerification.IsVerified(emailVerified: null, xmsEdov, trustWithoutClaim: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WithNeitherClaim_TheOperatorsChoiceDecides(bool trustWithoutClaim)
    {
        Assert.Equal(trustWithoutClaim, ExternalEmailVerification.IsVerified(emailVerified: null, xmsEdov: null, trustWithoutClaim));
    }
}

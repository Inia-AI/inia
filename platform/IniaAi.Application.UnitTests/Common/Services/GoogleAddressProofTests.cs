using IniaAi.Application.Common.Services;

namespace IniaAi.Application.UnitTests.Common.Services;

/// <summary>
/// Google is authoritative for a Gmail address and for any Google Workspace account (a token with an
/// <c>hd</c> claim); any other address may have changed hands since its Google account was made.
/// </summary>
public class GoogleAddressProofTests
{
    [Theory]
    [InlineData("someone@gmail.com", null)]
    [InlineData("someone@GMAIL.com", null)]
    [InlineData("someone@googlemail.com", null)]
    [InlineData("analyst@bank.example", "bank.example")]
    [InlineData("analyst@mail.bank.example", "bank.example")]
    [InlineData("analyst@bank-alias.example", "bank.example")]
    public void AGmailAddress_OrAnyAddressOfAWorkspaceAccount_IsProven(string email, string? hostedDomain)
    {
        Assert.True(GoogleAddressProof.Proves(email, hostedDomain));
    }

    [Theory]
    [InlineData("analyst@bank.example", null)]
    [InlineData("analyst@bank.example", "")]
    [InlineData("someone@gmail.com.example", null)]
    [InlineData("not-an-address", null)]
    [InlineData(null, null)]
    public void AnyOtherAddress_IsNot(string? email, string? hostedDomain)
    {
        Assert.False(GoogleAddressProof.Proves(email, hostedDomain));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void GoogleVouches_ForAnUnprovenVerifiedAddress_OnlyWhileProvenAddressesAreNotRequired(bool provenAddressesOnly, bool vouches)
    {
        Assert.Equal(vouches, GoogleAddressProof.Vouches(emailVerified: true, "analyst@bank.example", hostedDomain: null, provenAddressesOnly));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GoogleVouches_ForAProvenAddressItMarksVerified_AndNeverForOneItDoesNot(bool provenAddressesOnly)
    {
        Assert.True(GoogleAddressProof.Vouches(emailVerified: true, "someone@gmail.com", hostedDomain: null, provenAddressesOnly));
        Assert.False(GoogleAddressProof.Vouches(emailVerified: false, "someone@gmail.com", hostedDomain: null, provenAddressesOnly));
    }
}

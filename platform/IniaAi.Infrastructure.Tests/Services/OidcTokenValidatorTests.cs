using System.Security.Claims;
using System.Security.Cryptography;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Configuration.Security;
using IniaAi.Infrastructure.Services;
using IniaAi.Infrastructure.Tests.Helpers;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace IniaAi.Infrastructure.Tests.Services;

public class OidcTokenValidatorTests : IDisposable
{
    private const string TestIssuer = "https://dev-123456.okta.com/oauth2/default";
    private const string TestClientId = "0oa1bcdef";
    private const string TestSubject = "00u1234567890";
    private const string TestEmail = "user@company.com";
    private const string TestName = "Test User";

    private readonly RSA _rsa;
    private readonly RsaSecurityKey _signingKey;

    public OidcTokenValidatorTests()
    {
        _rsa = OidcTestTokenHelper.CreateTestRsaKey();
        _signingKey = OidcTestTokenHelper.CreateSecurityKey(_rsa);
    }

    private OidcTokenValidator CreateValidator(
        string? issuer = null, string? clientId = null, RsaSecurityKey? key = null, bool trustUnverifiedEmail = false)
    {
        var settings = new OidcAuthSettings
        {
            Enabled = true,
            Issuer = issuer ?? TestIssuer,
            ClientId = clientId ?? TestClientId,
            TrustUnverifiedEmail = trustUnverifiedEmail,
        };
        StaticConfigurationManager<OpenIdConnectConfiguration> configManager = OidcTestTokenHelper.CreateConfigManager(
            key ?? _signingKey, issuer ?? TestIssuer);
        return new OidcTokenValidator(settings, configManager);
    }

    [Fact]
    public async Task ValidToken_ReturnsValidResult()
    {
        OidcTokenValidator validator = CreateValidator();
        string token = OidcTestTokenHelper.CreateSignedToken(
            _signingKey, TestIssuer, TestClientId, TestSubject, TestEmail, TestName);

        OidcTokenValidationResult result = await validator.ValidateTokenAsync(token, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ValidToken_ExtractsAllClaims()
    {
        OidcTokenValidator validator = CreateValidator();
        string token = OidcTestTokenHelper.CreateSignedToken(
            _signingKey, TestIssuer, TestClientId, TestSubject, TestEmail, TestName);

        OidcTokenValidationResult result = await validator.ValidateTokenAsync(token, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
        Assert.Equal(TestEmail, result.Email);
        Assert.Equal(TestSubject, result.Subject);
        Assert.Equal(TestName, result.Name);
    }

    public static TheoryData<Claim[], bool, bool> VerificationCases => new()
    {
        { [new Claim("email_verified", "true")], false, true },
        { [new Claim("email_verified", "false")], true, false },
        { [new Claim("xms_edov", "1")], false, true },
        { [], false, false },
        { [], true, true },
    };

    [Theory]
    [MemberData(nameof(VerificationCases))]
    public async Task ValidToken_SaysWhetherTheProviderVouchesForTheEmail(Claim[] verificationClaims, bool trustUnverifiedEmail, bool expected)
    {
        OidcTokenValidator validator = CreateValidator(trustUnverifiedEmail: trustUnverifiedEmail);
        string token = OidcTestTokenHelper.CreateSignedToken(
            _signingKey, TestIssuer, TestClientId, TestSubject, TestEmail, TestName, verificationClaims: verificationClaims);

        OidcTokenValidationResult result = await validator.ValidateTokenAsync(token, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
        Assert.Equal(expected, result.EmailVerified);
    }

    [Fact]
    public async Task ExpiredToken_ReturnsInvalid()
    {
        OidcTokenValidator validator = CreateValidator();
        string token = OidcTestTokenHelper.CreateSignedToken(
            _signingKey, TestIssuer, TestClientId, TestSubject, TestEmail, TestName,
            issuedAt: DateTime.UtcNow.AddHours(-2),
            expires: DateTime.UtcNow.AddHours(-1));

        OidcTokenValidationResult result = await validator.ValidateTokenAsync(token, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public async Task WrongAudience_ReturnsInvalid()
    {
        OidcTokenValidator validator = CreateValidator();
        string token = OidcTestTokenHelper.CreateSignedToken(
            _signingKey, TestIssuer, "wrong-client-id", TestSubject, TestEmail);

        OidcTokenValidationResult result = await validator.ValidateTokenAsync(token, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public async Task WrongIssuer_ReturnsInvalid()
    {
        OidcTokenValidator validator = CreateValidator();
        string token = OidcTestTokenHelper.CreateSignedToken(
            _signingKey, "https://evil.example.com/oauth2/default",
            TestClientId, TestSubject, TestEmail);

        OidcTokenValidationResult result = await validator.ValidateTokenAsync(token, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public async Task MetadataNamingAnotherIssuer_ReturnsInvalid()
    {
        const string otherIssuer = "https://evil.example.com/oauth2/default";
        var settings = new OidcAuthSettings { Enabled = true, Issuer = TestIssuer, ClientId = TestClientId };
        var validator = new OidcTokenValidator(settings, OidcTestTokenHelper.CreateConfigManager(_signingKey, otherIssuer));
        string token = OidcTestTokenHelper.CreateSignedToken(_signingKey, otherIssuer, TestClientId, TestSubject, TestEmail);

        OidcTokenValidationResult result = await validator.ValidateTokenAsync(token, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task MetadataIssuerThatDiffersOnlyByATrailingSlash_IsAccepted()
    {
        var settings = new OidcAuthSettings { Enabled = true, Issuer = TestIssuer + "/", ClientId = TestClientId };
        var validator = new OidcTokenValidator(settings, OidcTestTokenHelper.CreateConfigManager(_signingKey, TestIssuer));
        string token = OidcTestTokenHelper.CreateSignedToken(_signingKey, TestIssuer, TestClientId, TestSubject, TestEmail);

        OidcTokenValidationResult result = await validator.ValidateTokenAsync(token, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public async Task InvalidSignature_ReturnsInvalid()
    {
        using var differentRsa = RSA.Create(2048);
        var differentKey = new RsaSecurityKey(differentRsa) { KeyId = "different-kid" };

        OidcTokenValidator validator = CreateValidator();
        string token = OidcTestTokenHelper.CreateSignedToken(
            differentKey, TestIssuer, TestClientId, TestSubject, TestEmail);

        OidcTokenValidationResult result = await validator.ValidateTokenAsync(token, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public async Task MalformedToken_ReturnsInvalid()
    {
        OidcTokenValidator validator = CreateValidator();

        OidcTokenValidationResult result = await validator.ValidateTokenAsync("not.a.valid.jwt", TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public async Task EmptyToken_ReturnsInvalid()
    {
        OidcTokenValidator validator = CreateValidator();

        OidcTokenValidationResult result = await validator.ValidateTokenAsync("", TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
        Assert.NotEmpty(result.Error);
    }

    public void Dispose()
    {
        _rsa.Dispose();
    }
}

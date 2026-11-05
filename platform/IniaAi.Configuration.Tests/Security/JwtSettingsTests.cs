using IniaAi.Configuration.Security;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Configuration.Tests.Security;

public class JwtSettingsTests
{
    [Fact]
    public void SectionName_IsJwt()
    {
        Assert.Equal("Jwt", JwtSettings.SectionName);
    }

    [Fact]
    public void FullyConfiguredSettings_AreValid()
    {
        Assert.Empty(Validate(CreateValidSettings()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void MissingOrNonPositiveTokenValidity_FailsValidation(int tokenValidityInMinutes)
    {
        JwtSettings settings = CreateValidSettings();
        settings.TokenValidityInMinutes = tokenValidityInMinutes;

        ValidationResult failure = Assert.Single(Validate(settings));
        Assert.Contains(nameof(JwtSettings.TokenValidityInMinutes), failure.ErrorMessage);
    }

    [Fact]
    public void MissingSecretKey_FailsValidation()
    {
        JwtSettings settings = CreateValidSettings();
        settings.SecretKey = string.Empty;

        ValidationResult failure = Assert.Single(Validate(settings));
        Assert.Contains(nameof(JwtSettings.SecretKey), failure.ErrorMessage);
    }

    [Fact]
    public void SecretKeyShorterThan32Bytes_FailsValidationNamingTheKey()
    {
        JwtSettings settings = CreateValidSettings();
        settings.SecretKey = new string('k', 31);

        ValidationResult failure = Assert.Single(Validate(settings));
        Assert.Contains("Jwt:SecretKey", failure.ErrorMessage);
    }

    [Fact]
    public void SecretKeyOf32Bytes_IsValid()
    {
        JwtSettings settings = CreateValidSettings();
        settings.SecretKey = new string('k', 32);

        Assert.Empty(Validate(settings));
    }

    [Fact]
    public void MissingIssuer_FailsValidation()
    {
        JwtSettings settings = CreateValidSettings();
        settings.Issuer = string.Empty;

        ValidationResult failure = Assert.Single(Validate(settings));
        Assert.Contains(nameof(JwtSettings.Issuer), failure.ErrorMessage);
    }

    private static JwtSettings CreateValidSettings()
    {
        return new JwtSettings
        {
            TokenValidityInMinutes = 60,
            SecretKey = "a-strong-enough-test-signing-secret",
            Issuer = "https://issuer.test",
        };
    }

    private static List<ValidationResult> Validate(JwtSettings settings)
    {
        var results = new List<ValidationResult>();
        _ = Validator.TryValidateObject(settings, new ValidationContext(settings), results, validateAllProperties: true);
        return results;
    }
}

using IniaAi.Application.Auditing;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Common.Constants;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Services;
using IniaAi.Infrastructure.Tests.Helpers;
using Microsoft.AspNetCore.Identity;
using IniaAi.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Infrastructure.Tests.Services;

public class ExternalLoginUserResolverTests : IDisposable
{
    private const string PROVIDER = ExternalLoginProviders.OIDC;
    private const string USER_EMAIL = "analyst@company.com";
    private const string LOOKALIKE_TARGET_EMAIL = "analyst@bank.example.com";

    private readonly InMemoryIdentityHarness _harness = new();
    private readonly CapturingLogger<LoggingAuthenticationAuditLog> _auditLog = new();
    private readonly ExternalLoginUserResolver _resolver;

    public ExternalLoginUserResolverTests()
    {
        _resolver = new ExternalLoginUserResolver(
            _harness.UserManager,
            NullLogger<ExternalLoginUserResolver>.Instance,
            new LoggingAuthenticationAuditLog(_auditLog));
    }

    public void Dispose()
    {
        _harness.Dispose();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-at-sign")]
    public async Task ResolveAsync_WithoutValidEmail_FailsWithMissingEmailAsync(string? email)
    {
        ExternalLoginUserResolution resolution = await _resolver.ResolveAsync(PROVIDER, email, emailVerified: true, []);

        Assert.False(resolution.Succeeded);
        Assert.Null(resolution.User);
        Assert.Equal(ExternalLoginFailure.MissingEmail, resolution.Failure);
    }

    [Fact]
    public async Task ResolveAsync_DomainNotInAllowlist_FailsWithDomainNotAllowedAsync()
    {
        _ = await _harness.CreateUserAsync(USER_EMAIL);

        ExternalLoginUserResolution resolution = await _resolver.ResolveAsync(PROVIDER, USER_EMAIL, emailVerified: true, ["inia.ai"]);

        Assert.False(resolution.Succeeded);
        Assert.Equal(ExternalLoginFailure.DomainNotAllowed, resolution.Failure);
        Assert.Equal("company.com", resolution.RejectedDomain);
    }

    [Fact]
    public async Task ResolveAsync_DomainInAllowlistDifferentCase_ResolvesUserAsync()
    {
        User user = await _harness.CreateUserAsync(USER_EMAIL);

        ExternalLoginUserResolution resolution = await _resolver.ResolveAsync(PROVIDER, USER_EMAIL, emailVerified: true, ["COMPANY.COM"]);

        Assert.True(resolution.Succeeded);
        Assert.Equal(user.Id, resolution.User!.Id);
        Assert.Null(resolution.Failure);
    }

    [Fact]
    public async Task ResolveAsync_EmptyAllowlist_AllowsAnyDomainAsync()
    {
        User user = await _harness.CreateUserAsync(USER_EMAIL);

        ExternalLoginUserResolution resolution = await _resolver.ResolveAsync(PROVIDER, USER_EMAIL, emailVerified: true, []);

        Assert.True(resolution.Succeeded);
        Assert.Equal(user.Id, resolution.User!.Id);
    }

    [Fact]
    public async Task ResolveAsync_UnknownEmail_FailsWithUserNotFoundAsync()
    {
        ExternalLoginUserResolution resolution = await _resolver.ResolveAsync(PROVIDER, "nobody@company.com", emailVerified: true, []);

        Assert.False(resolution.Succeeded);
        Assert.Equal(ExternalLoginFailure.UserNotFound, resolution.Failure);
    }

    [Fact]
    public async Task ResolveAsync_SystemUser_FailsWithSystemUserAsync()
    {
        _ = await _harness.CreateUserAsync(USER_EMAIL, isSystem: true);

        ExternalLoginUserResolution resolution = await _resolver.ResolveAsync(PROVIDER, USER_EMAIL, emailVerified: true, []);

        Assert.False(resolution.Succeeded);
        Assert.Equal(ExternalLoginFailure.SystemUser, resolution.Failure);
    }

    [Fact]
    public async Task ResolveAsync_LockedOutUser_FailsWithLockedOutAsync()
    {
        User user = await _harness.CreateUserAsync(USER_EMAIL);
        _ = await _harness.UserManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(15));

        ExternalLoginUserResolution resolution = await _resolver.ResolveAsync(PROVIDER, USER_EMAIL, emailVerified: true, []);

        Assert.False(resolution.Succeeded);
        Assert.Equal(ExternalLoginFailure.LockedOut, resolution.Failure);
    }

    [Fact]
    public async Task ResolveAsync_AdministrativelyDisabledUser_FailsWithLockedOutAsync()
    {
        User user = await _harness.CreateUserAsync(USER_EMAIL);
        user.Disable();
        _ = await _harness.UserManager.UpdateAsync(user);

        ExternalLoginUserResolution resolution = await _resolver.ResolveAsync(PROVIDER, USER_EMAIL, emailVerified: true, []);

        Assert.False(resolution.Succeeded);
        Assert.Equal(ExternalLoginFailure.LockedOut, resolution.Failure);
    }

    [Fact]
    public async Task ResolveAsync_ExistingUser_ResolvesRegardlessOfEmailCasingAsync()
    {
        User user = await _harness.CreateUserAsync(USER_EMAIL);

        ExternalLoginUserResolution resolution = await _resolver.ResolveAsync(PROVIDER, "Analyst@Company.com", emailVerified: true, []);

        Assert.True(resolution.Succeeded);
        Assert.Equal(user.Id, resolution.User!.Id);
    }

    /// <summary>
    /// The upper-case and Kelvin-sign spellings are the account's address: NFC makes the Kelvin sign
    /// the letter K. The long s is another address that only the lookup's upper-casing folds onto it.
    /// </summary>
    public static TheoryData<string, ExternalLoginFailure?> LookalikeSpellings => new()
    {
        { EmailLookalikes.UpperCase(LOOKALIKE_TARGET_EMAIL), null },
        { EmailLookalikes.LongS(LOOKALIKE_TARGET_EMAIL), ExternalLoginFailure.UserNotFound },
        { EmailLookalikes.KelvinSign(LOOKALIKE_TARGET_EMAIL), null },
    };

    [Theory]
    [MemberData(nameof(LookalikeSpellings))]
    public async Task ResolveAsync_ProviderEmailTheLookupFoldsOntoTheAccount_SignsInOnlyAsTheSameAddressAsync(string providerEmail, ExternalLoginFailure? expectedFailure)
    {
        User user = await _harness.CreateUserAsync(LOOKALIKE_TARGET_EMAIL);

        ExternalLoginUserResolution resolution = await _resolver.ResolveAsync(PROVIDER, providerEmail, emailVerified: true, []);

        Assert.Equal(expectedFailure, resolution.Failure);
        Assert.Equal(expectedFailure is null ? user.Id : null, resolution.User?.Id);
    }

    [Fact]
    public async Task LinkLoginIfNeededAsync_NewLogin_AddsLoginRowAsync()
    {
        User user = await _harness.CreateUserAsync(USER_EMAIL);

        await _resolver.LinkLoginIfNeededAsync(user, ExternalLoginProviders.GOOGLE, "google-sub-1");

        IList<UserLoginInfo> logins = await _harness.UserManager.GetLoginsAsync(user);
        UserLoginInfo login = Assert.Single(logins);
        Assert.Equal(ExternalLoginProviders.GOOGLE, login.LoginProvider);
        Assert.Equal("google-sub-1", login.ProviderKey);
        Assert.Equal(ExternalLoginProviders.GOOGLE, login.ProviderDisplayName);
    }

    [Fact]
    public async Task LinkLoginIfNeededAsync_AlreadyLinked_IsIdempotentAsync()
    {
        User user = await _harness.CreateUserAsync(USER_EMAIL);

        await _resolver.LinkLoginIfNeededAsync(user, PROVIDER, "sub-42");
        await _resolver.LinkLoginIfNeededAsync(user, PROVIDER, "sub-42");

        IList<UserLoginInfo> logins = await _harness.UserManager.GetLoginsAsync(user);
        _ = Assert.Single(logins);
    }

    [Fact]
    public async Task LinkLoginIfNeededAsync_KeyLinkedToOtherUser_SwallowsFailureAsync()
    {
        User first = await _harness.CreateUserAsync(USER_EMAIL);
        User second = await _harness.CreateUserAsync("other@company.com");
        await _resolver.LinkLoginIfNeededAsync(first, PROVIDER, "shared-sub");

        await _resolver.LinkLoginIfNeededAsync(second, PROVIDER, "shared-sub");

        IList<UserLoginInfo> secondLogins = await _harness.UserManager.GetLoginsAsync(second);
        Assert.Empty(secondLogins);
        IList<UserLoginInfo> firstLogins = await _harness.UserManager.GetLoginsAsync(first);
        _ = Assert.Single(firstLogins);
    }

    [Theory]
    [InlineData(ExternalLoginProviders.GOOGLE)]
    [InlineData(ExternalLoginProviders.OIDC)]
    public async Task ResolveAsync_WithAnUnverifiedEmail_FailsWithEmailNotVerifiedAsync(string provider)
    {
        _ = await _harness.CreateUserAsync(USER_EMAIL);

        ExternalLoginUserResolution resolution = await _resolver.ResolveAsync(provider, USER_EMAIL, emailVerified: false, []);

        Assert.False(resolution.Succeeded);
        Assert.Equal(ExternalLoginFailure.EmailNotVerified, resolution.Failure);
    }

    [Fact]
    public async Task ResolveAsync_RecordsEachOutcomeInTheAudit()
    {
        User user = await _harness.CreateUserAsync(USER_EMAIL);

        _ = await _resolver.ResolveAsync(PROVIDER, USER_EMAIL, emailVerified: true, [], "203.0.113.7");
        _ = await _resolver.ResolveAsync(PROVIDER, USER_EMAIL, emailVerified: false, [], "203.0.113.7");

        Assert.Collection(
            _auditLog.Entries,
            success =>
            {
                Assert.Equal(AuthenticationAuditKind.SignInSucceeded, success.Value("AuditKind"));
                Assert.Equal(user.Id, success.Value("UserId"));
                Assert.Equal("203.0.113.7", success.Value("ClientAddress"));
            },
            refusal => Assert.Equal(AuthenticationAuditKind.SignInRefused, refusal.Value("AuditKind")));
    }

    [Fact]
    public async Task ResolveAsync_UnknownVerifiedAddress_SignsUpThroughTheSeam()
    {
        var signUp = new SignUpThroughHarness(_harness);
        ExternalLoginUserResolver resolver = ResolverWith(signUp);

        ExternalLoginUserResolution resolution = await resolver.ResolveAsync(PROVIDER, "newcomer@company.com", emailVerified: true, []);

        Assert.True(resolution.Succeeded);
        Assert.Equal("newcomer@company.com", resolution.User!.Email);
        Assert.Equal([(PROVIDER, "newcomer@company.com")], signUp.Asked);
    }

    [Fact]
    public async Task ResolveAsync_SignUpIsNeverAsked_ForAnUnverifiedAddressADisallowedDomainOrALookalike()
    {
        _ = await _harness.CreateUserAsync(LOOKALIKE_TARGET_EMAIL);
        var signUp = new SignUpThroughHarness(_harness);
        ExternalLoginUserResolver resolver = ResolverWith(signUp);

        ExternalLoginUserResolution unverified = await resolver.ResolveAsync(PROVIDER, "newcomer@company.com", emailVerified: false, []);
        ExternalLoginUserResolution disallowed = await resolver.ResolveAsync(PROVIDER, "newcomer@company.com", emailVerified: true, ["inia.ai"]);
        ExternalLoginUserResolution lookalike = await resolver.ResolveAsync(PROVIDER, EmailLookalikes.LongS(LOOKALIKE_TARGET_EMAIL), emailVerified: true, []);

        Assert.Equal(ExternalLoginFailure.EmailNotVerified, unverified.Failure);
        Assert.Equal(ExternalLoginFailure.DomainNotAllowed, disallowed.Failure);
        Assert.Equal(ExternalLoginFailure.UserNotFound, lookalike.Failure);
        Assert.Empty(signUp.Asked);
    }

    [Fact]
    public async Task ResolveAsync_ARefusedSignUp_FailsWithTheSignUpsOwnError()
    {
        ExternalLoginUserResolution resolution = await ResolverWith(new RefusesEverySignUp())
            .ResolveAsync(PROVIDER, "someone@mailinator.com", emailVerified: true, []);

        Assert.Equal(ExternalLoginFailure.SignUpRefused, resolution.Failure);
        Assert.Equal("disposable_email", resolution.SignUpRefusal?.Error);
        Assert.Equal("Use an address you'll keep.", resolution.SignUpRefusal?.ErrorDescription);
    }

    [Fact]
    public async Task ResolveAsync_UnknownAddress_WithTheDefaultSignUp_StaysUserNotFound()
    {
        ExternalLoginUserResolution resolution = await ResolverWith(new NoExternalSignUp())
            .ResolveAsync(PROVIDER, "newcomer@company.com", emailVerified: true, []);

        Assert.Equal(ExternalLoginFailure.UserNotFound, resolution.Failure);
        Assert.Null(await _harness.UserManager.FindByEmailAsync("newcomer@company.com"));
    }

    private ExternalLoginUserResolver ResolverWith(IExternalSignUp signUp)
    {
        return new ExternalLoginUserResolver(
            _harness.UserManager,
            NullLogger<ExternalLoginUserResolver>.Instance,
            new LoggingAuthenticationAuditLog(_auditLog),
            signUp);
    }

    private sealed class SignUpThroughHarness(InMemoryIdentityHarness harness) : IExternalSignUp
    {
        public List<(string Provider, string Email)> Asked { get; } = [];

        public async Task<ExternalSignUpResult> SignUpAsync(string provider, string email, CancellationToken cancellationToken = default)
        {
            Asked.Add((provider, email));

            return ExternalSignUpResult.Created(await harness.CreateUserAsync(email));
        }
    }

    private sealed class RefusesEverySignUp : IExternalSignUp
    {
        public Task<ExternalSignUpResult> SignUpAsync(string provider, string email, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ExternalSignUpResult.Refused("disposable_email", "Use an address you'll keep."));
        }
    }
}

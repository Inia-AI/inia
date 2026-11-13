using IniaAi.Application.Common;
using Bunit;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection.Mocks;
using IniaAi.WebApp.Components.Account;
using IniaAi.WebApp.Components.Account.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.WebApp.Tests.Components.Account;

/// <summary>
/// The forgot-password and resend-confirmation pages mail the address stored on the account. The
/// account lookup matches a typed address through Identity's normalizer, which folds case and
/// Unicode lookalikes onto the stored address, so a mail to the typed address could carry the
/// account's link to another mailbox. A system account gets the answer an unknown address gets.
/// </summary>
public sealed class AccountMailRecipientTests : SignedInRenderTest
{
    private const string ACCOUNT_EMAIL = "analyst@bank.example.com";
    private const string UNKNOWN_EMAIL = "nobody@bank.example.com";
    private const string ADMIN_BASE_URL = "https://admin.example.com";
    private const string PASSWORD = "Quiet-Test-Passw0rd"; // gitleaks:allow

    private readonly RecordingOutgoingMail _mail = new(isConfigured: true);

    public AccountMailRecipientTests()
    {
        _ = new IdentityBuilder(typeof(User), typeof(IdentityRole), Services).AddDefaultTokenProviders();
        _ = Services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [AppAddresses.ADMIN_BASE_URL_KEY] = ADMIN_BASE_URL })
            .Build());
        _ = Services.AddSingleton<IOutgoingMail>(_mail);
        _ = Services.AddSingleton<IEmailSender<User>, IdentityMailSender>();
        _ = Services.AddSingleton<AccountLinks>();
        _ = Services.AddScoped<IdentityRedirectManager>();
        _ = Services.AddSingleton<NavigationManager>(new StaticRenderNavigationManager(ADMIN_BASE_URL));
    }

    [Theory]
    [MemberData(nameof(EmailLookalikes.Of), ACCOUNT_EMAIL, MemberType = typeof(EmailLookalikes))]
    public async Task ForgotPassword_ForAnAddressThatOnlyNormalizesToTheAccount_MailsTheStoredAddress(string typed)
    {
        await CreateAccountAsync();

        _ = await SubmitForgotPasswordAsync(typed);

        Assert.Equal(ACCOUNT_EMAIL, Assert.Single(_mail.Sent).To);
    }

    [Fact]
    public async Task ForgotPassword_ForASystemAccount_AnswersAsForAnUnknownAddressAndSendsNothing()
    {
        await CreateAccountAsync(isSystem: true);

        NavigationException system = await SubmitForgotPasswordAsync(ACCOUNT_EMAIL);
        NavigationException unknown = await SubmitForgotPasswordAsync(UNKNOWN_EMAIL);

        Assert.Equal(unknown.Location, system.Location);
        Assert.Empty(_mail.Sent);
    }

    [Theory]
    [MemberData(nameof(EmailLookalikes.Of), ACCOUNT_EMAIL, MemberType = typeof(EmailLookalikes))]
    public async Task ResendEmailConfirmation_ForAnAddressThatOnlyNormalizesToTheAccount_MailsTheStoredAddress(string typed)
    {
        await CreateAccountAsync();

        _ = await SubmitResendEmailConfirmationAsync(typed);

        Assert.Equal(ACCOUNT_EMAIL, Assert.Single(_mail.Sent).To);
    }

    [Fact]
    public async Task ResendEmailConfirmation_ForASystemAccount_AnswersAsForAnUnknownAddressAndSendsNothing()
    {
        await CreateAccountAsync(isSystem: true);

        string system = await SubmitResendEmailConfirmationAsync(ACCOUNT_EMAIL);
        string unknown = await SubmitResendEmailConfirmationAsync(UNKNOWN_EMAIL);

        Assert.Equal(unknown, system);
        Assert.Empty(_mail.Sent);
    }

    private async Task CreateAccountAsync(bool isSystem = false)
    {
        await SeedAsync(new Organization(ORGANIZATION_NAME, ORGANIZATION_ID));
        IdentityResult created = await Services.GetRequiredService<UserManager<User>>()
            .CreateAsync(new User(ORGANIZATION_ID) { UserName = ACCOUNT_EMAIL, Email = ACCOUNT_EMAIL, EmailConfirmed = true, IsSystem = isSystem }, PASSWORD);
        Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(error => error.Description)));
    }

    /// <returns>The redirect the page ends the request with.</returns>
    private async Task<NavigationException> SubmitForgotPasswordAsync(string typed)
    {
        IRenderedComponent<ForgotPassword> page = Render<ForgotPassword>();
        page.Find("input").Change(typed);

        return await Assert.ThrowsAsync<NavigationException>(() => page.Find("form").SubmitAsync());
    }

    /// <returns>The status message the page shows after the submit.</returns>
    private async Task<string> SubmitResendEmailConfirmationAsync(string typed)
    {
        IRenderedComponent<ResendEmailConfirmation> page = Render<ResendEmailConfirmation>(parameters => parameters.AddCascadingValue<HttpContext>(new DefaultHttpContext()));
        page.Find("input").Change(typed);
        await page.Find("form").SubmitAsync();

        return page.Find("[role=alert]").TextContent;
    }
}

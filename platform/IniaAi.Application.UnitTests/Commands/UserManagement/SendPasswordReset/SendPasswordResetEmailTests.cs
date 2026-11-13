using IniaAi.Application.Commands.UserManagement.SendPasswordReset;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Configuration.Security;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.RegularExpressions;

namespace IniaAi.Application.UnitTests.Commands.UserManagement.SendPasswordReset;

public class SendPasswordResetEmailTests
{
    private const string EMAIL = "analyst@bank.example.com";
    private const string FRONTEND = "https://app.example.com";
    private const string RESET_URL = $"{FRONTEND}{SendPasswordResetEmailCommandHandler.RESET_PATH}";
    private const string NEW_PASSWORD = "N3w-Passw0rd!";
    private const string PASSWORD = "0ld-Passw0rd!";

    [Fact]
    public async Task SendPasswordResetEmail_ForExistingUser_SendsOneResetEmail()
    {
        RecordingOutgoingMail outbox = new(isConfigured: true);
        using ServiceProvider services = BuildServices(outbox, FRONTEND);
        using IServiceScope scope = services.CreateScope();
        UserManager<User> users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        User user = await users.CreateAccountAsync(EMAIL, PASSWORD);

        _ = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new SendPasswordResetEmailCommand(EMAIL), TestContext.Current.CancellationToken);

        SentEmail email = Assert.Single(outbox.Sent);
        Assert.Equal(EMAIL, email.To);
        Assert.Equal("Reset your password", email.Subject);
        string linkPrefix = $"{RESET_URL}?email={WebUtility.UrlEncode(EMAIL)}&token=";
        Match link = Regex.Match(WebUtility.HtmlDecode(email.HtmlBody), $"{Regex.Escape(linkPrefix)}(?<token>[^']+)'");
        Assert.True(link.Success, email.HtmlBody);
        IdentityResult reset = await users.ResetPasswordAsync(user, WebUtility.UrlDecode(link.Groups["token"].Value), NEW_PASSWORD);
        Assert.True(reset.Succeeded, string.Join(", ", reset.Errors.Select(error => error.Description)));
    }

    [Theory]
    [MemberData(nameof(EmailLookalikes.Of), EMAIL, MemberType = typeof(EmailLookalikes))]
    public async Task SendPasswordResetEmail_ForAnAddressThatOnlyNormalizesToTheAccount_MailsTheStoredAddress(string typed)
    {
        RecordingOutgoingMail outbox = new(isConfigured: true);
        using ServiceProvider services = BuildServices(outbox, FRONTEND);
        using IServiceScope scope = services.CreateScope();
        _ = await scope.ServiceProvider.GetRequiredService<UserManager<User>>().CreateAccountAsync(EMAIL, PASSWORD);

        _ = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new SendPasswordResetEmailCommand(typed), TestContext.Current.CancellationToken);

        SentEmail email = Assert.Single(outbox.Sent);
        Assert.Equal(EMAIL, email.To);
        Assert.Contains($"{RESET_URL}?email={WebUtility.UrlEncode(EMAIL)}&token=", WebUtility.HtmlDecode(email.HtmlBody));
    }

    [Fact]
    public async Task SendPasswordResetEmail_WhilePasswordSignInIsOff_SendsNothing()
    {
        RecordingOutgoingMail outbox = new(isConfigured: true);
        using ServiceProvider services = BuildServices(outbox, FRONTEND, passwordSignIn: false);
        using IServiceScope scope = services.CreateScope();
        _ = await scope.ServiceProvider.GetRequiredService<UserManager<User>>().CreateAccountAsync(EMAIL, PASSWORD);

        _ = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new SendPasswordResetEmailCommand(EMAIL), TestContext.Current.CancellationToken);

        Assert.Empty(outbox.Sent);
    }

    [Fact]
    public async Task SendPasswordResetEmail_ForUnknownEmail_SendsNothing()
    {
        RecordingOutgoingMail outbox = new(isConfigured: true);
        using ServiceProvider services = BuildServices(outbox, FRONTEND);
        using IServiceScope scope = services.CreateScope();

        _ = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new SendPasswordResetEmailCommand("nobody@example.com"), TestContext.Current.CancellationToken);

        Assert.Empty(outbox.Sent);
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, FRONTEND)]
    public async Task SendPasswordResetEmail_WithoutAFrontendAddressOrMail_SendsNothing(bool mailConfigured, string? frontend)
    {
        RecordingOutgoingMail outbox = new(mailConfigured);
        using ServiceProvider services = BuildServices(outbox, frontend);
        using IServiceScope scope = services.CreateScope();
        _ = await scope.ServiceProvider.GetRequiredService<UserManager<User>>().CreateAccountAsync(EMAIL, PASSWORD);

        _ = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new SendPasswordResetEmailCommand(EMAIL), TestContext.Current.CancellationToken);

        Assert.Empty(outbox.Sent);
    }

    [Fact]
    public async Task SendPasswordResetEmail_WhenTheMailCannotBeSent_SucceedsAsForAnUnknownEmail()
    {
        using ServiceProvider services = BuildServices(new FailingOutgoingMail(), FRONTEND);
        using IServiceScope scope = services.CreateScope();
        _ = await scope.ServiceProvider.GetRequiredService<UserManager<User>>().CreateAccountAsync(EMAIL, PASSWORD);

        Exception? failure = await Record.ExceptionAsync(() => scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new SendPasswordResetEmailCommand(EMAIL), TestContext.Current.CancellationToken));

        Assert.Null(failure);
    }

    [Fact]
    public async Task SendPasswordResetEmail_WithMailOff_DoesNotRunTheMailStepAtAll()
    {
        CapturingLogger<SendPasswordResetEmailCommandHandler> mailStep = new();
        using ServiceProvider services = BuildServices(new RecordingOutgoingMail(isConfigured: false), FRONTEND, mailStep);
        using IServiceScope scope = services.CreateScope();
        _ = await scope.ServiceProvider.GetRequiredService<UserManager<User>>().CreateAccountAsync(EMAIL, PASSWORD);

        _ = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new SendPasswordResetEmailCommand(EMAIL), TestContext.Current.CancellationToken);

        Assert.Empty(mailStep.Entries);
    }

    [Fact]
    public async Task SendPasswordResetEmail_ForAnAccountWithNoPassword_SendsNothing()
    {
        RecordingOutgoingMail outbox = new(isConfigured: true);
        using ServiceProvider services = BuildServices(outbox, FRONTEND);
        using IServiceScope scope = services.CreateScope();
        _ = await scope.ServiceProvider.GetRequiredService<UserManager<User>>().CreateAccountAsync(EMAIL);

        _ = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new SendPasswordResetEmailCommand(EMAIL), TestContext.Current.CancellationToken);

        Assert.Empty(outbox.Sent);
    }

    [Fact]
    public async Task SendPasswordResetEmail_ForASystemUser_SendsNothing()
    {
        RecordingOutgoingMail outbox = new(isConfigured: true);
        using ServiceProvider services = BuildServices(outbox, FRONTEND);
        using IServiceScope scope = services.CreateScope();
        _ = await scope.ServiceProvider.GetRequiredService<UserManager<User>>().CreateAccountAsync(EMAIL, isSystem: true);

        _ = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new SendPasswordResetEmailCommand(EMAIL), TestContext.Current.CancellationToken);

        Assert.Empty(outbox.Sent);
    }

    private static ServiceProvider BuildServices(IOutgoingMail outbox, string? frontend, CapturingLogger<SendPasswordResetEmailCommandHandler>? mailStepLog = null, bool passwordSignIn = true)
    {
        return IdentityTestDatabase.Build(services =>
        {
            _ = services.Configure<PasswordAuthSettings>(settings => settings.Enabled = passwordSignIn);
            _ = services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [SendPasswordResetEmailCommandHandler.FRONTEND_BASE_URL_KEY] = frontend,
                })
                .Build());
            _ = services.AddSingleton<IOutgoingMail>(outbox);
            if (mailStepLog is not null)
            {
                _ = services.AddSingleton<ILogger<SendPasswordResetEmailCommandHandler>>(mailStepLog);
            }
        });
    }

    private sealed class FailingOutgoingMail : IOutgoingMail
    {
        public bool IsConfigured => true;

        public Task SendAsync(OutgoingMailMessage message)
        {
            throw new InvalidOperationException("The mail provider refused the message.");
        }
    }
}

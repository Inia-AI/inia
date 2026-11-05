using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection.Mocks;
using IniaAi.WebApp.Components.Account;

namespace IniaAi.WebApp.Tests.Components.Account;

/// <summary>
/// The admin app's account mail goes out through the deployment's mail, and nowhere when mail is off.
/// </summary>
public class IdentityMailSenderTests
{
    private const string EMAIL = "admin@example.com";
    private const string LINK = "https://admin.example.com/Account/ResetPassword?code=abc";

    [Fact]
    public async Task AResetLink_GoesOutThroughTheDeploymentsMail()
    {
        RecordingOutgoingMail mail = new(isConfigured: true);

        await new IdentityMailSender(mail, new CapturingLogger<IdentityMailSender>()).SendPasswordResetLinkAsync(new User(), EMAIL, LINK);

        (string to, _, string body) = Assert.Single(mail.Sent);
        Assert.Equal(EMAIL, to);
        Assert.Contains(LINK, body);
    }

    [Fact]
    public async Task WithMailOff_NothingIsSentAndAnErrorIsLogged()
    {
        RecordingOutgoingMail mail = new(isConfigured: false);
        CapturingLogger<IdentityMailSender> log = new();

        await new IdentityMailSender(mail, log).SendPasswordResetLinkAsync(new User(), EMAIL, LINK);

        Assert.Empty(mail.Sent);
        Assert.Contains(log.Entries, entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Error);
        Assert.DoesNotContain(log.Entries, entry => entry.Message.Contains(EMAIL));
    }
}

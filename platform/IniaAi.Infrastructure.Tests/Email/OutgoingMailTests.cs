using Amazon.SimpleEmailV2.Model;
using IniaAi.Infrastructure;
using IniaAi.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Configuration.Email;
using IniaAi.Testing.DependencyInjection.Mocks;
using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IniaAi.Infrastructure.Tests.Email;

/// <summary>
/// Mail is on only when a deployment names a provider and gives it what it needs to send. Every
/// feature that mails (the second-factor code, the password reset) asks this first and stays off
/// otherwise, instead of failing at the moment a user waits for the mail.
/// </summary>
public class OutgoingMailTests
{
    [Fact]
    public void WithNoProvider_MailIsOff()
    {
        Assert.False(Build([]).IsConfigured);
    }

    [Fact]
    public async Task WithMailOff_SendingRefusesNamingTheKey()
    {
        InvalidOperationException refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Build([]).SendAsync("analyst@example.com", "Subject", "<p>Body</p>"));

        Assert.Contains(OutgoingMail.PROVIDER_KEY, refusal.Message);
    }

    [Fact]
    public void AnSmtpProviderWithAHostAndSender_TurnsMailOn()
    {
        IOutgoingMail mail = Build(new()
        {
            [OutgoingMail.PROVIDER_KEY] = "Smtp",
            ["Smtp:Host"] = "smtp.example.com",
            ["Smtp:FromEmail"] = "noreply@example.com",
        });

        Assert.True(mail.IsConfigured);
    }

    [Fact]
    public void AnSmtpProviderWithoutAHost_LeavesMailOff()
    {
        IOutgoingMail mail = Build(new()
        {
            [OutgoingMail.PROVIDER_KEY] = "Smtp",
            ["Smtp:FromEmail"] = "noreply@example.com",
        });

        Assert.False(mail.IsConfigured);
    }

    [Fact]
    public void ASendGridProviderWithAKeyAndSender_TurnsMailOn()
    {
        IOutgoingMail mail = Build(new()
        {
            [OutgoingMail.PROVIDER_KEY] = "sendgrid",
            ["SendGrid:ApiKey"] = "a-sendgrid-key",
            ["SendGrid:FromEmail"] = "noreply@example.com",
        });

        Assert.True(mail.IsConfigured);
    }

    [Fact]
    public void ASendGridProviderWithoutAKey_LeavesMailOff()
    {
        IOutgoingMail mail = Build(new()
        {
            [OutgoingMail.PROVIDER_KEY] = "SendGrid",
            ["SendGrid:FromEmail"] = "noreply@example.com",
        });

        Assert.False(mail.IsConfigured);
    }

    [Fact]
    public void AnSesProviderWithASender_TurnsMailOn()
    {
        IOutgoingMail mail = Build(new()
        {
            [OutgoingMail.PROVIDER_KEY] = "Ses",
            ["Ses:FromEmail"] = "no-reply@app.example.com",
        });

        Assert.True(mail.IsConfigured);
    }

    [Fact]
    public void AnSesProviderWithoutASender_LeavesMailOff()
    {
        Assert.False(Build(new() { [OutgoingMail.PROVIDER_KEY] = "Ses" }).IsConfigured);
    }

    [Fact]
    public void AnSesMessage_CarriesTheSenderTheReplyToAddressAndTheHeaders()
    {
        var outgoing = new OutgoingMailMessage("customer@example.com", "Your Inia sign-in code", "<p>Body</p>")
        {
            ReplyTo = "support@example.com",
            Headers = new Dictionary<string, string> { [OutgoingMailMessage.AUTO_SUBMITTED_HEADER] = OutgoingMailMessage.AUTO_GENERATED },
        };

        SendEmailRequest request = SesEmailSender.Compose(outgoing, new SesSettings { FromEmail = "no-reply@app.example.com", FromName = "Inia AI" });

        Assert.Equal("Inia AI <no-reply@app.example.com>", request.FromEmailAddress);
        Assert.Equal("customer@example.com", Assert.Single(request.Destination.ToAddresses));
        Assert.Equal("support@example.com", Assert.Single(request.ReplyToAddresses));
        Assert.Equal("Your Inia sign-in code", request.Content.Simple.Subject.Data);
        Assert.Equal("<p>Body</p>", request.Content.Simple.Body.Html.Data);
        MessageHeader header = Assert.Single(request.Content.Simple.Headers);
        Assert.Equal((OutgoingMailMessage.AUTO_SUBMITTED_HEADER, OutgoingMailMessage.AUTO_GENERATED), (header.Name, header.Value));
    }

    [Fact]
    public void AnSesMessageWithoutASenderName_SendsFromTheBareAddress()
    {
        SendEmailRequest request = SesEmailSender.Compose(
            new OutgoingMailMessage("customer@example.com", "Subject", "<p>Body</p>"),
            new SesSettings { FromEmail = "no-reply@app.example.com" });

        Assert.Equal("no-reply@app.example.com", request.FromEmailAddress);
        Assert.Empty(request.ReplyToAddresses);
    }

    [Theory]
    [InlineData("Inia, AI", "\"Inia, AI\" <no-reply@app.example.com>")]
    [InlineData("Ini\u00E4", "=?utf-8?B?SW5pw6Q=?= <no-reply@app.example.com>")]
    public void AnSesSenderNameThatIsNotPlainText_IsQuotedOrEncoded(string name, string from)
    {
        SendEmailRequest request = SesEmailSender.Compose(
            new OutgoingMailMessage("customer@example.com", "Subject", "<p>Body</p>"),
            new SesSettings { FromEmail = "no-reply@app.example.com", FromName = name });

        Assert.Equal(from, request.FromEmailAddress);
    }

    [Fact]
    public void AnSesMessageToAnInternationalDomain_GoesToItsAsciiForm()
    {
        SendEmailRequest request = SesEmailSender.Compose(
            new OutgoingMailMessage("analyst@b\u00FCcher.example", "Subject", "<p>Body</p>") { ReplyTo = "support@b\u00FCcher.example" },
            new SesSettings { FromEmail = "no-reply@app.example.com" });

        Assert.Equal("analyst@xn--bcher-kva.example", Assert.Single(request.Destination.ToAddresses));
        Assert.Equal("support@xn--bcher-kva.example", Assert.Single(request.ReplyToAddresses));
    }

    [Fact]
    public void AnUnknownProvider_LeavesMailOff()
    {
        Assert.False(Build(new() { [OutgoingMail.PROVIDER_KEY] = "Carrier pigeon" }).IsConfigured);
    }

    [Fact]
    public async Task ALineBreakInAnyHeaderValue_IsStrippedBeforeTheProviderSeesIt()
    {
        var provider = new RecordingEmailSender();
        var message = new OutgoingMailMessage("analyst@example.com\r\nBcc: attacker@example.com", "Your request #A1B2\r\nBcc: attacker@example.com", "<p>Body\r\nline</p>")
        {
            ReplyTo = "support@example.com\nBcc: attacker@example.com",
            Headers = new Dictionary<string, string> { [OutgoingMailMessage.AUTO_SUBMITTED_HEADER] = "auto-generated\r\nX-Injected: yes" },
        };

        await new OutgoingMail(provider).SendAsync(message);

        OutgoingMailMessage sent = Assert.Single(provider.Sent);
        Assert.Equal("analyst@example.comBcc: attacker@example.com", sent.To);
        Assert.Equal("Your request #A1B2Bcc: attacker@example.com", sent.Subject);
        Assert.Equal("support@example.comBcc: attacker@example.com", sent.ReplyTo);
        Assert.Equal("auto-generatedX-Injected: yes", sent.Headers[OutgoingMailMessage.AUTO_SUBMITTED_HEADER]);
        Assert.Equal("<p>Body\r\nline</p>", sent.HtmlBody);
    }

    [Fact]
    public void AnSmtpMessage_CarriesTheReplyToAddressAndTheHeaders()
    {
        var outgoing = new OutgoingMailMessage("customer@example.com", "Support replied to your request #A1B2C3D4", "<p>Body</p>")
        {
            ReplyTo = "support@example.com",
            Headers = new Dictionary<string, string> { [OutgoingMailMessage.AUTO_SUBMITTED_HEADER] = OutgoingMailMessage.AUTO_GENERATED },
        };

        using MailMessage message = SmtpEmailSender.Compose(outgoing, new SmtpSettings { FromEmail = "noreply@example.com", FromName = "Inia" });

        Assert.Equal("support@example.com", Assert.Single(message.ReplyToList).Address);
        Assert.Equal(OutgoingMailMessage.AUTO_GENERATED, message.Headers[OutgoingMailMessage.AUTO_SUBMITTED_HEADER]);
        Assert.Equal("customer@example.com", Assert.Single(message.To).Address);
        Assert.True(message.IsBodyHtml);
    }

    [Fact]
    public async Task ASendGridRefusal_FailsTheSend_InsteadOfPassingForSent()
    {
        using var http = new HttpClient(new AnsweringHandler(HttpStatusCode.Unauthorized));
        var sender = new SendGridEmailSender(
            Options.Create(new SendGridSettings { ApiKey = "SG.test", FromEmail = "noreply@example.com", FromName = "Inia" }),
            NullLogger<SendGridEmailSender>.Instance,
            http);

        InvalidOperationException refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sender.SendEmailAsync(new OutgoingMailMessage("customer@example.com", "Subject", "<p>Body</p>")));

        Assert.Contains("401", refusal.Message);
    }

    [Fact]
    public async Task ASendGridAcceptance_Sends()
    {
        using var http = new HttpClient(new AnsweringHandler(HttpStatusCode.Accepted));
        var sender = new SendGridEmailSender(
            Options.Create(new SendGridSettings { ApiKey = "SG.test", FromEmail = "noreply@example.com", FromName = "Inia" }),
            NullLogger<SendGridEmailSender>.Instance,
            http);

        await sender.SendEmailAsync(new OutgoingMailMessage("customer@example.com", "Subject", "<p>Body</p>"));
    }

    private sealed class AnsweringHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(string.Empty) });
        }
    }

    private static IOutgoingMail Build(Dictionary<string, string?> settings)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddOutgoingMail(configuration);
        return services.BuildServiceProvider().GetRequiredService<IOutgoingMail>();
    }
}

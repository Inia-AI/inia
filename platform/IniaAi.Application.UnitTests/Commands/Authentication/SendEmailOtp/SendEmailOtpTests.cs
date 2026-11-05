using System.Text.RegularExpressions;
using ErrorOr;
using IniaAi.Application.Commands.Authentication.SendEmailOtp;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Configuration.Security;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Services;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IniaAi.Application.UnitTests.Commands.Authentication.SendEmailOtp;

/// <summary>
/// The second-factor code by email goes out through the deployment's mail, and only when mail is
/// set up. No log line on its way carries the user's address.
/// </summary>
public sealed partial class SendEmailOtpTests : IDisposable
{
    private const string EMAIL = "analyst@example.com";
    private const string CLIENT = "203.0.113.7";

    private readonly ServiceProvider _services;
    private readonly IServiceScope _scope;
    private readonly UserManager<User> _users;
    private readonly MfaTokenService _mfaTokens;
    private readonly CapturingLogger<SendEmailOtpCommandHandler> _log = new();

    public SendEmailOtpTests()
    {
        ServiceCollection services = new();
        _ = services.AddLogging();
        _ = services.AddDataProtection();
        _ = services.AddDbContext<UserDataDbContext>(options => options.UseInMemoryDatabase($"email-otp-{Guid.NewGuid()}"));
        _ = services.AddIdentityCore<User>()
            .AddEntityFrameworkStores<UserDataDbContext>()
            .AddDefaultTokenProviders();
        _services = services.BuildServiceProvider();
        _scope = _services.CreateScope();
        _users = _scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        _mfaTokens = new MfaTokenService(
            _services.GetRequiredService<IDataProtectionProvider>(),
            NullLogger<MfaTokenService>.Instance,
            Options.Create(new TwoFactorSettings()));
    }

    public void Dispose()
    {
        _scope.Dispose();
        _services.Dispose();
    }

    [Fact]
    public async Task TheCode_ReachesTheUsersAddressAndSignsIn()
    {
        User user = await CreateUserAsync();
        RecordingOutgoingMail outbox = new(isConfigured: true);

        ErrorOr<Success> result = await SendAsync(user, outbox);

        Assert.False(result.IsError);
        SentEmail mail = Assert.Single(outbox.Sent);
        Assert.Equal(EMAIL, mail.To);
        string code = SixDigits().Match(mail.HtmlBody).Value;
        Assert.True(await _users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultEmailProvider, code));
    }

    [Fact]
    public async Task WithMailOff_NoCodeIsSentAndTheRequestIsRefused()
    {
        User user = await CreateUserAsync();
        RecordingOutgoingMail outbox = new(isConfigured: false);

        ErrorOr<Success> result = await SendAsync(user, outbox);

        Assert.Equal(SendEmailOtpCommandHandler.UNAVAILABLE, result.FirstError.Code);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Empty(outbox.Sent);
    }

    [Fact]
    public async Task NoLogLine_CarriesTheUsersAddress()
    {
        User user = await CreateUserAsync();

        _ = await SendAsync(user, new RecordingOutgoingMail(isConfigured: true));
        _ = await SendAsync(user, new RecordingOutgoingMail(isConfigured: true));

        Assert.NotEmpty(_log.Entries);
        Assert.All(_log.Entries, entry =>
        {
            Assert.DoesNotContain(EMAIL, entry.Message);
            Assert.DoesNotContain(entry.Values, value => value.Value is string text && text.Contains(EMAIL));
        });
    }

    private async Task<ErrorOr<Success>> SendAsync(User user, IOutgoingMail mail)
    {
        var handler = new SendEmailOtpCommandHandler(
            _mfaTokens,
            _users,
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new TwoFactorSettings()),
            mail,
            _log);

        string mfaToken = _mfaTokens.GenerateMfaToken(user.Id, EMAIL, CLIENT);
        return await handler.Handle(new SendEmailOtpCommand(mfaToken, CLIENT), TestContext.Current.CancellationToken);
    }

    private async Task<User> CreateUserAsync()
    {
        User user = new() { UserName = EMAIL, Email = EMAIL, EmailConfirmed = true };
        IdentityResult created = await _users.CreateAsync(user);
        Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(error => error.Description)));
        return user;
    }

    [GeneratedRegex(@"\b\d{6}\b")]
    private static partial Regex SixDigits();
}

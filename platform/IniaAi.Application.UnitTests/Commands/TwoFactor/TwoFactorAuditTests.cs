using System.Text.Encodings.Web;
using ErrorOr;
using IniaAi.Application.Auditing;
using IniaAi.Application.Commands.TwoFactor.DisableTotp;
using IniaAi.Application.Commands.TwoFactor.EnableTotp;
using IniaAi.Application.Commands.TwoFactor.RegenerateRecoveryCodes;
using IniaAi.Application.Commands.TwoFactor.RemoveAllTrustedDevices;
using IniaAi.Application.Commands.TwoFactor.RemoveTrustedDevice;
using IniaAi.Application.Commands.TwoFactor.SetupTotp;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Responses.TwoFactor;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Application.UnitTests.Commands.TwoFactor;

/// <summary>
/// Every change to an account's second factor leaves one authentication audit record, with the
/// user and the client address, through the audit seam a compliance module can replace.
/// </summary>
public sealed class TwoFactorAuditTests : IDisposable
{
    private const string ORGANIZATION_ID = "org-two-factor-audit";
    private const string CLIENT = "203.0.113.7";

    private readonly CapturingLogger<LoggingAuthenticationAuditLog> _auditLog = new();
    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _services;
    private readonly IServiceScope _scope;
    private readonly UserManager<User> _users;
    private readonly ISender _sender;

    public TwoFactorAuditTests()
    {
        ServiceCollection services = new();
        _ = services.AddLogging();
        _ = services.AddDataProtection();
        _ = services.AddTestDatabaseServices();
        _ = services.AddSingleton(UrlEncoder.Default);
        _ = services.AddIdentityCore<User>()
            .AddEntityFrameworkStores<UserDataDbContext>()
            .AddDefaultTokenProviders();
        _ = services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        _ = services.AddApplication();
        _ = services.AddSingleton<IAuthenticationAuditLog>(new LoggingAuthenticationAuditLog(_auditLog));
        _services = services.BuildServiceProvider();
        _scope = _services.CreateScope();
        _users = _scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        _sender = _scope.ServiceProvider.GetRequiredService<ISender>();
    }

    public void Dispose()
    {
        _scope.Dispose();
        _services.Dispose();
    }

    [Fact]
    public async Task EachTwoFactorChange_LeavesOneAuditRecord()
    {
        User user = new() { UserName = "analyst@example.com", Email = "analyst@example.com", EmailConfirmed = true };
        Assert.True((await _users.CreateAsync(user)).Succeeded);
        UserDataDbContext db = _scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        db.Set<UserTrustedDevice>().AddRange(
            new UserTrustedDevice { UserId = user.Id, DeviceFingerprint = "laptop", TrustedUntil = DateTime.UtcNow.AddDays(30) },
            new UserTrustedDevice { UserId = user.Id, DeviceFingerprint = "phone", TrustedUntil = DateTime.UtcNow.AddDays(30) });
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        string laptopId = db.Set<UserTrustedDevice>().Single(d => d.DeviceFingerprint == "laptop").Id;

        AssertSucceeded(await _sender.Send(new SetupTotpCommand(ORGANIZATION_ID, user.Id, CLIENT, null), TestContext.Current.CancellationToken));
        AssertSucceeded(await _sender.Send(new EnableTotpCommand(ORGANIZATION_ID, user.Id, await CurrentCodeAsync(user), CLIENT, null), TestContext.Current.CancellationToken));
        AssertSucceeded(await _sender.Send(new RegenerateRecoveryCodesCommand(ORGANIZATION_ID, user.Id, CLIENT, null), TestContext.Current.CancellationToken));
        AssertSucceeded(await _sender.Send(new RemoveTrustedDeviceCommand(ORGANIZATION_ID, user.Id, laptopId, CLIENT, null), TestContext.Current.CancellationToken));
        AssertSucceeded(await _sender.Send(new RemoveAllTrustedDevicesCommand(ORGANIZATION_ID, user.Id, CLIENT, null), TestContext.Current.CancellationToken));
        AssertSucceeded(await _sender.Send(new DisableTotpCommand(ORGANIZATION_ID, user.Id, await CurrentCodeAsync(user), CLIENT, null), TestContext.Current.CancellationToken));

        Assert.Equal(
            [
                AuthenticationAuditKind.TwoFactorSetupStarted,
                AuthenticationAuditKind.TwoFactorEnabled,
                AuthenticationAuditKind.RecoveryCodesRegenerated,
                AuthenticationAuditKind.TrustedDeviceRemoved,
                AuthenticationAuditKind.AllTrustedDevicesRemoved,
                AuthenticationAuditKind.TwoFactorDisabled,
            ],
            _auditLog.Entries.Select(entry => (AuthenticationAuditKind)entry.Value("AuditKind")!));
        Assert.All(_auditLog.Entries, entry =>
        {
            Assert.Equal(user.Id, entry.Value("UserId"));
            Assert.Equal(CLIENT, entry.Value("ClientAddress"));
        });
    }

    [Fact]
    public async Task TwoFactorChangeIdentityRefuses_IsAnErrorAndNotAudited()
    {
        User settingUp = await CreateWithAuthenticatorAsync("first@example.com");
        User enabled = await CreateWithAuthenticatorAsync("second@example.com");
        AssertSucceeded(await _sender.Send(new EnableTotpCommand(ORGANIZATION_ID, enabled.Id, await CurrentCodeAsync(enabled), CLIENT, null), TestContext.Current.CancellationToken));
        await GiveNameIdentityRejectsAsync(settingUp, enabled);

        ErrorOr<EnableTotpResponse> enabling = await _sender.Send(new EnableTotpCommand(ORGANIZATION_ID, settingUp.Id, await CurrentCodeAsync(settingUp), CLIENT, null), TestContext.Current.CancellationToken);
        ErrorOr<EnableTotpResponse> regenerating = await _sender.Send(new RegenerateRecoveryCodesCommand(ORGANIZATION_ID, enabled.Id, CLIENT, null), TestContext.Current.CancellationToken);
        ErrorOr<Success> disabling = await _sender.Send(new DisableTotpCommand(ORGANIZATION_ID, enabled.Id, await CurrentCodeAsync(enabled), CLIENT, null), TestContext.Current.CancellationToken);

        Assert.True(enabling.IsError);
        Assert.True(regenerating.IsError);
        Assert.True(disabling.IsError);
        Assert.Equal(
            [AuthenticationAuditKind.TwoFactorSetupStarted, AuthenticationAuditKind.TwoFactorSetupStarted, AuthenticationAuditKind.TwoFactorEnabled],
            _auditLog.Entries.Select(entry => (AuthenticationAuditKind)entry.Value("AuditKind")!));
    }

    private async Task<User> CreateWithAuthenticatorAsync(string email)
    {
        User user = new() { UserName = email, Email = email, EmailConfirmed = true };
        Assert.True((await _users.CreateAsync(user)).Succeeded);
        AssertSucceeded(await _sender.Send(new SetupTotpCommand(ORGANIZATION_ID, user.Id, CLIENT, null), TestContext.Current.CancellationToken));
        return user;
    }

    private async Task GiveNameIdentityRejectsAsync(params User[] users)
    {
        foreach (User user in users)
        {
            user.UserName = $"Example Admin {user.Email}";
        }

        _ = await _scope.ServiceProvider.GetRequiredService<UserDataDbContext>().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static void AssertSucceeded<T>(ErrorOr<T> result)
    {
        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : null);
    }

    private async Task<string> CurrentCodeAsync(User user)
    {
        return AuthenticatorCodes.Current((await _users.GetAuthenticatorKeyAsync(user))!);
    }
}

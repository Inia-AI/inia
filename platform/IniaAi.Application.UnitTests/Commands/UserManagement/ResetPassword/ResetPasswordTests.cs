using ErrorOr;
using IniaAi.Application.Commands.UserManagement.ResetPassword;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Configuration.Security;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.EntityFrameworkCore.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace IniaAi.Application.UnitTests.Commands.UserManagement.ResetPassword;

/// <summary>
/// The page a reset mail opens sends the address, the token and the new password back. A wrong or
/// used token and an unknown address get the same answer, so the page does not reveal accounts.
/// </summary>
public sealed class ResetPasswordTests : IDisposable
{
    private const string EMAIL = "analyst@example.com";
    private const string OLD_PASSWORD = "0ld-Passw0rd!";
    private const string NEW_PASSWORD = "N3w-Passw0rd!";

    private readonly ServiceProvider _services;
    private readonly IServiceScope _scope;
    private readonly UserManager<User> _users;
    private readonly ISender _sender;

    public ResetPasswordTests()
    {
        _services = IdentityTestDatabase.Build();
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
    public async Task AValidToken_SetsTheNewPassword()
    {
        User user = await _users.CreateAccountAsync(EMAIL, OLD_PASSWORD);
        string token = await _users.GeneratePasswordResetTokenAsync(user);

        ErrorOr<Success> result = await _sender.Send(new ResetPasswordCommand(EMAIL, token, NEW_PASSWORD), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.True(await _users.CheckPasswordAsync(user, NEW_PASSWORD));
        Assert.False(await _users.CheckPasswordAsync(user, OLD_PASSWORD));
    }

    [Fact]
    public async Task AValidToken_WhilePasswordSignInIsOff_GetsTheBadLinkAnswer_AndChangesNothing()
    {
        using ServiceProvider services = IdentityTestDatabase.Build(services => services.Configure<PasswordAuthSettings>(settings => settings.Enabled = false));
        using IServiceScope scope = services.CreateScope();
        UserManager<User> users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        User user = await users.CreateAccountAsync(EMAIL, OLD_PASSWORD);
        string token = await users.GeneratePasswordResetTokenAsync(user);

        ErrorOr<Success> result = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new ResetPasswordCommand(EMAIL, token, NEW_PASSWORD), TestContext.Current.CancellationToken);

        Assert.Equal(ResetPasswordCommandHandler.INVALID_LINK, result.FirstError.Code);
        Assert.True(await users.CheckPasswordAsync(user, OLD_PASSWORD));
    }

    [Fact]
    public async Task AReset_EndsTheUsersApiSessions()
    {
        User user = await _users.CreateAccountAsync(EMAIL, OLD_PASSWORD);
        string token = await _users.GeneratePasswordResetTokenAsync(user);
        UserDataDbContext db = _scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        _ = db.Tokens.Add(new OpenIddictEntityFrameworkCoreToken { Subject = user.Id, Status = Statuses.Valid, Type = TokenTypeIdentifiers.AccessToken });
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _ = await _sender.Send(new ResetPasswordCommand(EMAIL, token, NEW_PASSWORD), TestContext.Current.CancellationToken);

        Assert.Equal(Statuses.Revoked, (await db.Tokens.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task ARefusedReset_KeepsTheUsersApiSessions()
    {
        User user = await _users.CreateAccountAsync(EMAIL, OLD_PASSWORD);
        UserDataDbContext db = _scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        _ = db.Tokens.Add(new OpenIddictEntityFrameworkCoreToken { Subject = user.Id, Status = Statuses.Valid, Type = TokenTypeIdentifiers.AccessToken });
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _ = await _sender.Send(new ResetPasswordCommand(EMAIL, "any-token", NEW_PASSWORD), TestContext.Current.CancellationToken);

        Assert.Equal(Statuses.Valid, (await db.Tokens.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task AUsedToken_IsRefused()
    {
        User user = await _users.CreateAccountAsync(EMAIL, OLD_PASSWORD);
        string token = await _users.GeneratePasswordResetTokenAsync(user);
        _ = await _sender.Send(new ResetPasswordCommand(EMAIL, token, NEW_PASSWORD), TestContext.Current.CancellationToken);

        ErrorOr<Success> again = await _sender.Send(new ResetPasswordCommand(EMAIL, token, "An0ther-Passw0rd!"), TestContext.Current.CancellationToken);

        Assert.Equal(ResetPasswordCommandHandler.INVALID_LINK, again.FirstError.Code);
    }

    [Fact]
    public async Task AnUnknownAddress_GetsTheSameAnswerAsABadToken()
    {
        _ = await _users.CreateAccountAsync(EMAIL, OLD_PASSWORD);

        ErrorOr<Success> unknown = await _sender.Send(new ResetPasswordCommand("nobody@example.com", "any-token", NEW_PASSWORD), TestContext.Current.CancellationToken);
        ErrorOr<Success> badToken = await _sender.Send(new ResetPasswordCommand(EMAIL, "any-token", NEW_PASSWORD), TestContext.Current.CancellationToken);

        Assert.Equal(ResetPasswordCommandHandler.INVALID_LINK, unknown.FirstError.Code);
        Assert.Equal(unknown.FirstError, badToken.FirstError);
    }

    [Fact]
    public async Task AWeakPassword_IsRefusedWithTheRuleItBreaks()
    {
        User user = await _users.CreateAccountAsync(EMAIL, OLD_PASSWORD);
        string token = await _users.GeneratePasswordResetTokenAsync(user);

        ErrorOr<Success> result = await _sender.Send(new ResetPasswordCommand(EMAIL, token, "short"), TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.All(result.Errors, error => Assert.Equal(ErrorType.Validation, error.Type));
        Assert.True(await _users.CheckPasswordAsync(user, OLD_PASSWORD));
    }
}

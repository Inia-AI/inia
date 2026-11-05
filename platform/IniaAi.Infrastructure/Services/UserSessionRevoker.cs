using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Common.Text;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// Marks the user's stored tokens revoked. The API checks each token's stored entry on every
/// request, so a revoked access token is refused at once. The admin app runs no OpenIddict
/// server, so this writes the token table directly, as the OpenIddict store's own revoke does.
/// </summary>
public sealed class UserSessionRevoker : IUserSessionRevoker
{
    private readonly UserDataDbContext _db;
    private readonly ILogger<UserSessionRevoker> _logger;

    public UserSessionRevoker(UserDataDbContext db, ILogger<UserSessionRevoker> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task RevokeAllAsync(string userId, CancellationToken cancellationToken = default)
    {
        int revoked = await _db.Tokens
            .Where(token => token.Subject == userId && token.Status == Statuses.Valid)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.Status, Statuses.Revoked), cancellationToken);

        _logger.LogInformation("Revoked {Count} API tokens of user {UserId}", revoked, LogSanitizer.ForLog(userId));
    }
}

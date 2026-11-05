using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Common.Services;

/// <summary>
/// Resolves user ids to display names by reading users from the UserData context. Prefers a
/// person's full name, then email, then username, falling back to the raw id only as a last resort.
/// </summary>
public class UserDisplayNameResolver : IUserDisplayNameResolver
{
    private static readonly IReadOnlyDictionary<string, string> _empty =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private readonly IUserDataUnitOfWork _userDataUnitOfWork;

    public UserDisplayNameResolver(IUserDataUnitOfWork userDataUnitOfWork)
    {
        _userDataUnitOfWork = userDataUnitOfWork;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> ResolveAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken = default)
    {
        HashSet<string> distinctIds = [.. userIds.Where(id => !string.IsNullOrWhiteSpace(id))];

        if (distinctIds.Count == 0)
        {
            return _empty;
        }

        List<User> users = await _userDataUnitOfWork.Repository<User>()
            .GetAll()
            .AsNoTracking()
            .Where(user => distinctIds.Contains(user.Id))
            .ToListAsync(cancellationToken);

        return users.ToDictionary(user => user.Id, DisplayName, StringComparer.Ordinal);
    }

    private static string DisplayName(User user)
    {
        string fullName = user.GetFullName();
        if (!string.IsNullOrWhiteSpace(fullName))
        {
            return fullName;
        }

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            return user.Email;
        }

        if (!string.IsNullOrWhiteSpace(user.UserName))
        {
            return user.UserName;
        }

        return user.Id;
    }
}
